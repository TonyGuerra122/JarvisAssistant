"""
Serviço local de voz clonada do J.A.R.V.I.S (XTTS-v2, via coqui-tts).

Uso:
    python voice_server.py prepare <audio.mp3|wav>   -> gera reference.wav limpo (Demucs + redução de ruído)
    python voice_server.py download                  -> baixa o modelo XTTS-v2 (~1,8 GB)
    python voice_server.py serve [--port 8020]       -> API HTTP

API:
    GET  /health      {"available": true, "device": "cuda"}
    POST /synthesize  {"text": "...", "speed": 1.0, "language": "pt"}  -> audio/wav (24 kHz, 16 bits)

A voz de referência deve ser de alguém que autorizou o uso.
O modelo XTTS-v2 usa a licença Coqui Public Model License (só uso não comercial).
"""

# Sem "from __future__ import annotations": o FastAPI precisa ver o tipo real do Req
# (definido dentro de serve) para ler o corpo JSON da requisição.
import argparse
import io
import os
import sys
import threading
from pathlib import Path

os.environ.setdefault("COQUI_TOS_AGREED", "1")  # aceite da licença CPML (uso não comercial)

HERE = Path(__file__).resolve().parent
DEFAULT_REF = HERE / "reference.wav"
MODEL_NAME = "tts_models/multilingual/multi-dataset/xtts_v2"
SAMPLE_RATE = 24000


def clean(audio, rate: int):
    """
    Limpa a voz de referência (o XTTS copia o ruído junto com a voz):
      1. Demucs (htdemucs) separa a voz de música/ruído de fundo;
      2. passa-altas em 70 Hz tira ronco e zumbido;
      3. noisereduce remove o chiado constante que sobrar.
    Devolve mono em SAMPLE_RATE.
    """
    import noisereduce as nr
    import torch
    import torchaudio.functional as F
    from demucs.apply import apply_model
    from demucs.pretrained import get_model

    device = "cuda" if torch.cuda.is_available() else "cpu"
    model = get_model("htdemucs")
    model.eval()

    wav = torch.from_numpy(audio).float()
    if rate != model.samplerate:
        wav = F.resample(wav, rate, model.samplerate)

    stereo = wav.unsqueeze(0).repeat(model.audio_channels, 1)
    mean, std = wav.mean(), wav.std() + 1e-8
    with torch.no_grad():
        sources = apply_model(model, ((stereo - mean) / std)[None], device=device,
                              split=True, overlap=0.25, progress=False)[0]
    vocals = (sources[model.sources.index("vocals")] * std + mean).mean(0).cpu()

    vocals = F.resample(vocals, model.samplerate, SAMPLE_RATE)
    vocals = F.highpass_biquad(vocals, SAMPLE_RATE, 70.0)
    return nr.reduce_noise(y=vocals.numpy(), sr=SAMPLE_RATE, stationary=True, prop_decrease=0.75)


def prepare(src: Path, dst: Path, do_clean: bool = True) -> None:
    """Limpa (opcional), converte para WAV mono 24 kHz, normaliza e corta o silêncio das pontas."""
    import numpy as np
    import soundfile as sf

    audio, rate = sf.read(str(src), always_2d=True)
    audio = audio.mean(axis=1)  # mono

    if do_clean:
        print("limpando o áudio (Demucs + redução de ruído)...", flush=True)
        audio, rate = clean(audio, rate), SAMPLE_RATE
    elif rate != SAMPLE_RATE:
        import torch
        import torchaudio.functional as F

        audio = F.resample(torch.from_numpy(audio).float(), rate, SAMPLE_RATE).numpy()
        rate = SAMPLE_RATE

    # Corta silêncio nas pontas (abaixo de -40 dB do pico)
    peak = float(np.max(np.abs(audio))) or 1.0
    loud = np.where(np.abs(audio) > peak * 0.01)[0]
    if loud.size:
        audio = audio[max(0, loud[0] - rate // 10): loud[-1] + rate // 10]

    audio = audio / peak * 0.9
    sf.write(str(dst), audio.astype("float32"), rate, subtype="PCM_16")
    print(f"reference: {dst} ({len(audio) / rate:.1f}s)")


def download() -> None:
    """Baixa o modelo XTTS-v2 (~1,8 GB) para o cache, sem carregar na GPU."""
    from TTS.utils.manage import ModelManager

    path = ModelManager().download_model(MODEL_NAME)
    print(f"model: {path[0] if isinstance(path, tuple) else path}")


def serve(port: int, ref: Path) -> None:
    import numpy as np
    import soundfile as sf
    import torch
    import uvicorn
    from fastapi import FastAPI, HTTPException
    from fastapi.responses import Response
    from pydantic import BaseModel
    from TTS.api import TTS

    if not ref.exists():
        sys.exit(f"Voz de referência não encontrada: {ref}")

    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"Carregando XTTS-v2 em {device}...", flush=True)
    model = TTS(MODEL_NAME).to(device).synthesizer.tts_model

    # A "impressão" da voz é calculada uma vez só, no início.
    latent, embedding = model.get_conditioning_latents(
        audio_path=[str(ref)], gpt_cond_len=30, max_ref_length=60
    )

    app = FastAPI(title="J.A.R.V.I.S voice")
    lock = threading.Lock()  # a GPU atende uma frase por vez

    class Req(BaseModel):
        text: str
        speed: float = 1.0
        language: str = "pt"
        temperature: float = 0.65

    @app.get("/health")
    def health():
        return {"available": True, "device": device, "reference": ref.name}

    @app.post("/synthesize")
    def synthesize(req: Req):
        text = req.text.strip()
        if not text:
            raise HTTPException(400, "texto vazio")
        with lock, torch.inference_mode():
            out = model.inference(
                text,
                req.language,
                latent,
                embedding,
                speed=max(0.5, min(2.0, req.speed)),
                temperature=req.temperature,
                enable_text_splitting=True,
            )
        buf = io.BytesIO()
        sf.write(buf, np.asarray(out["wav"], dtype="float32"), SAMPLE_RATE, format="WAV", subtype="PCM_16")
        return Response(buf.getvalue(), media_type="audio/wav")

    print(f"Pronto em http://127.0.0.1:{port}", flush=True)
    uvicorn.run(app, host="127.0.0.1", port=port, log_level="warning")


def main() -> None:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("prepare")
    p.add_argument("source", type=Path)
    p.add_argument("--out", type=Path, default=DEFAULT_REF)
    p.add_argument("--no-clean", action="store_true", help="não remove ruído (áudio já limpo)")

    sub.add_parser("download")

    s = sub.add_parser("serve")
    s.add_argument("--port", type=int, default=8020)
    s.add_argument("--ref", type=Path, default=DEFAULT_REF)

    args = parser.parse_args()
    if args.cmd == "prepare":
        prepare(args.source, args.out, do_clean=not args.no_clean)
    elif args.cmd == "download":
        download()
    else:
        serve(args.port, args.ref)


if __name__ == "__main__":
    main()
