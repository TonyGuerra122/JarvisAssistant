# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Windows-only, fully local voice assistant in Brazilian Portuguese. A WPF shell hosts an HTML/JS HUD in WebView2; the assistant logic talks to a locally installed [OpenJarvis](https://github.com/open-jarvis/OpenJarvis) server (`jarvis serve`, backed by Ollama + Whisper + Kokoro). The app auto-installs OpenJarvis and its dependencies on first run.

Code comments, log messages, UI strings and the README are written in Portuguese (pt-BR) — keep new ones in Portuguese.

## Commands

```powershell
dotnet build JarvisAssistant.slnx       # build both projects
dotnet run --project GUI                # run the app (produces JarvisAssistant.exe)
dotnet publish GUI -c Release           # release executable (win-x64)
```

- Target: .NET 10 (`net10.0-windows`), WPF. There is no test project and no linter configured.
- The HUD can be opened directly in a browser (`GUI/web/index.html`): when `window.chrome.webview` is absent, `app.js` runs a `Demo` mode that fakes boot/replies — useful for UI-only work.
- Set `"Debug": true` in `%APPDATA%\JarvisAssistant\config.json` to enable DevTools (F12) in the HUD.
- Voice-clone service can be run standalone: `python voice_server.py prepare <audio> | download | serve --port 8020` (deps in `ServiceLibrary/VoiceClone/pyproject.toml`, managed with `uv`, PyTorch CUDA 12.4 index).

## Architecture

Two projects in `JarvisAssistant.slnx`:

- **GUI** (WPF, `WinExe`) — thin shell. `Views/MainWindow.xaml.cs` creates the WebView2 (user data in `%LOCALAPPDATA%\JarvisAssistant\WebView2`), maps `GUI/web/` to the virtual host `https://jarvis.local`, and owns the `Assistant`.
- **ServiceLibrary** — all logic. No UI dependencies.

### HUD ↔ C# message protocol

Communication is JSON messages over WebView2 `postMessage`, camelCase (`MainWindow.PostToHud` serializes with `JsonNamingPolicy.CamelCase`). Adding a feature that crosses the boundary means touching both sides:

- JS → C# (`MainWindow.OnWebMessage` switch): `ready` (triggers `Assistant.BootAsync`), `ptt {down}`, `text {text}`, `weather {summary}` (weather is fetched from Open-Meteo in JS and pushed to C# for the LLM context), `minimize`, `exit`.
- C# → JS (`handle()` in `GUI/web/app.js`; `Assistant` receives an `Action<object> post` callback): `config`, `boot {id,text,status}`, `bootDone`, `state` (`booting|idle|listening|thinking|speaking|offline`), `level {value}` (mic/player VU), `caption {role,text}`, `status`.

`PostToHud` is thread-safe (marshals via `Dispatcher`), so `Assistant` can post from any thread.

### Assistant pipeline (`ServiceLibrary/Assistant.cs`)

Boot: find OpenJarvis at `OpenJarvisUrl` → install (`OpenJarvisInstaller`) and/or start server (`OpenJarvisServer`) → check model, STT, TTS, mic. Then the loop: mic audio → `POST /v1/speech/transcribe` → chat (`/v1/chat/completions`, with history, personality prompt, date/time/city/weather) → `CleanReply` strips `<think>` blocks and markdown → `SplitForSpeech` splits into sentences, each synthesized while the previous plays. Speaking over a reply interrupts it.

TTS has three backends with automatic fallback (clone → Kokoro via OpenJarvis → Windows SAPI → captions only), selected by `Voice` config: `Audio/SpeechPlayer`, `Audio/WindowsVoice`, `VoiceClone/VoiceCloneServer`. `Audio/AiVoiceEffect` applies pitch/speed and the "AI voice" effect (NAudio).

### Child processes

`OpenJarvisServer` and `VoiceCloneServer` launch hidden processes and kill their entire process tree on dispose — but **only processes they started themselves**; an already-running server is reused and left alone. `jarvis serve` is started via `cmd /c` with output redirected to a file (not a pipe) so it survives the app exiting; note the extra outer quotes required by `cmd /c` quoting rules.

### Installer (`ServiceLibrary/Setup/`)

`OpenJarvisInstaller` runs an ordered list of `InstallStep`s (in `Steps/`, one class per step — order matters, later steps use tool paths found by earlier ones via the shared `InstallerContext`). Each step is idempotent: `IsSatisfiedAsync` → `InstallAsync` → re-verify. `Required` steps abort on failure; optional ones become warnings (`StepStatus.Skipped`). Failures meant for the user should throw `InstallException` with a pt-BR message. Progress is reported per step `Id`, which is also the row id in the HUD boot screen.

- `InstallAsync` runs all steps; `EnsureRuntimeAsync` runs only the fast runtime subset (uv, speech, Ollama, model, voice clone) on every launch. When adding a step, decide whether it belongs in that subset.
- Installs use `winget` (`InstallerContext.WingetInstallAsync`, treats `0x8A15002B` as "already installed") and call `ProcessRunner.RefreshPath()` afterward.
- The installer mirrors the official OpenJarvis `install.ps1`.

### Config and paths

`AppConfig` (`%APPDATA%\JarvisAssistant\config.json`) is loaded and **re-written on every load** so new properties appear in users' files with defaults — add new options as properties with default initializers; the README's config table documents them. Paths live in `AppPaths`. Logs: `%LOCALAPPDATA%\JarvisAssistant\jarvis.log` (via `Log`), `openjarvis-serve.log`, `voice-clone.log`. OpenJarvis itself installs to `%LOCALAPPDATA%\OpenJarvis\` (shim `bin\jarvis.cmd`).

`ServiceLibrary/VoiceClone/{pyproject.toml,voice_server.py}` are copied to the build output; `VoiceCloneStep` copies them to `%LOCALAPPDATA%\JarvisAssistant\VoiceClone` where the venv lives. XTTS-v2 is under the Coqui CPML (non-commercial only).

### Comandos (`ServiceLibrary/Commands/`)

`CommandRunner` (partial: `.cs` núcleo/política/temporizadores, `.System.cs` volume/brilho/janelas/info/clipboard/captura, `.Files.cs` arquivos, `.Spotify.cs`, `.Reminders.cs` + `ReminderService.cs` temporizador/alarme/lembrete persistidos em disco) expõe ferramentas ao modelo (function calling) e executa só o que está na lista. Cada ação segue `AppConfig.ActionPolicies` (`auto|confirmar|bloquear`); `energia`, `fechar_programa` e `executar_powershell` sempre confirmam. Confirmação e avisos falados passam por callbacks do `Assistant` (`ConfirmAsync` / `NotifyAsync`) e recebem o `CancellationToken` do turno. Para adicionar uma ação: ferramenta em `BuildTools`, texto em `Describe`, caso em `DispatchAsync` e entrada em `AppConfig.DefaultActionPolicies`. Arquivos só dentro de `AllowedFolders`, e executáveis/scripts nunca abrem. Scripts PowerShell rodam via `RunPowerShellRawAsync` (`-Sta`, `-EncodedCommand`).

### Instalador e atualização (Velopack)

`GUI/Program.cs` é o ponto de entrada (`StartupObject`): chama `VelopackApp.Build().Run()` **antes** de criar o `App` (o Velopack trata os argumentos do instalador/atualizador). `GUI/Updates/UpdateService.cs` procura/baixa atualizações no GitHub Releases (`AppConfig.UpdateRepo`) e fala com o HUD pela mensagem `update {status,version}` (`downloading|ready|error`); o HUD responde `applyUpdate`. Só atua com o app instalado (`IsInstalled`); no `dotnet run` é inativo. O app instalado fica em `%LOCALAPPDATA%\JarvisAssistantApp` (packId `JarvisAssistantApp`) — de propósito diferente da pasta de dados `%LOCALAPPDATA%\JarvisAssistant`, porque desinstalar apaga a pasta do programa. Nunca grave dados ao lado do `.exe` (a pasta `current` é recriada a cada atualização). Release: tag `v*` dispara `.github/workflows/release.yml`, que usa `build/release.ps1` (publish self-contained win-x64 + `vpk pack`/`upload`); a versão vem da tag.

### Segundo plano e "sempre ouvindo"

GUI: `Tray/TrayIcon.cs` (NotifyIcon via `UseWindowsForms`; `Using Remove` dos namespaces do WinForms evita ambiguidade com o WPF) e `Tray/StartupRegistration.cs` (HKCU `Run` com `--startup`, só no app instalado). `MainWindow.OnClosing` cancela o fechamento e esconde a janela quando `RunInBackground` (só `Exit()` encerra de verdade; use-o em qualquer caminho que precise sair, inclusive atualização). `App` usa um `EventWaitHandle` para trazer a janela existente quando abrem o programa de novo. ServiceLibrary: `Audio/UtteranceDetector.cs` (VAD por energia, puro e testável) + `Audio/VoiceActivityListener.cs` (WaveIn aberto o tempo todo) + `Assistant.AlwaysListen.cs`: cada fala é transcrita e só vira comando com a palavra de ativação (`ExtractCommand`), na janela de acompanhamento (`FollowUpSeconds`) ou como "sim/não" de uma confirmação aberta (`TryVoiceConfirm`, só confirmações de uma linha). O microfone é ignorado quando o estado é speaking/thinking/listening (anti-eco). Nunca grave áudio em disco nem registre texto de falas descartadas. Encerrar por voz: `Assistant.Quit.cs` reconhece frases claras sem passar pelo modelo e há também a ferramenta `encerrar_jarvis`; ambas pedem o fechamento via `QuitRequested`, que só dispara depois da despedida.
