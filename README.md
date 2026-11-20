# J.A.R.V.I.S — Assistente de voz para Windows

Assistente pessoal por voz, em português do Brasil, que roda **100% local** no seu PC. Ele tem um HUD inspirado no J.A.R.V.I.S. e usa o [OpenJarvis](https://github.com/open-jarvis/OpenJarvis) como núcleo: um modelo de linguagem no Ollama para pensar, Whisper para ouvir e Kokoro para falar.

Na primeira execução, o app instala sozinho tudo de que precisa.

## Funcionalidades

- **Push-to-talk**: segure `Espaço`, fale e solte. O Jarvis transcreve, responde e lê a resposta em voz alta.
- **Modo texto**: aperte `Enter` para digitar em vez de falar.
- **Interrupção**: falar por cima corta a resposta atual.
- **Memória curta**: lembra as últimas trocas da conversa (configurável).
- **Contexto**: o modelo recebe a data, a hora, a sua cidade e o clima atual ([Open-Meteo](https://open-meteo.com)).
- **Três opções de voz**, com fallback automático:
  1. `openjarvis`: Kokoro (padrão, voz masculina pt-BR `pm_alex`)
  2. `clone`: voz clonada a partir de um áudio seu, com XTTS-v2
  3. `windows`: vozes nativas do Windows (SAPI)
- **Efeito de voz**: ajuste de tom, velocidade e um efeito leve de "voz de IA" (graves + eco curto).
- **Instalador automático**: Git, uv, Python, Build Tools C++, OpenJarvis, Ollama, modelo, eSpeak e a voz clonada, com o progresso de cada passo exibido na tela de boot.

## Requisitos

- Windows 10 1809 (build 17763) ou mais novo, com `winget`
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (para compilar)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (já vem no Windows 11)
- Microfone
- Opcional: GPU NVIDIA (acelera o Whisper e a voz clonada)

> A primeira instalação baixa vários GB (Build Tools, dependências Python, modelo do Ollama e, se você usar a voz clonada, o XTTS-v2 com ~1,8 GB). Ela pode levar bastante tempo.

## Como compilar e rodar

```powershell
git clone <url-do-repositorio>
cd JarvisAssistent
dotnet build JarvisAssistant.slnx
dotnet run --project GUI
```

Você também pode abrir `JarvisAssistant.slnx` no Visual Studio e rodar o projeto **GUI**.

Para gerar um executável:

```powershell
dotnet publish GUI -c Release
```

## Segundo plano e microfone sempre aberto

Com `RunInBackground`, o Jarvis vive na **bandeja do Windows**: fechar a janela só a esconde. O ícone abre o menu (Abrir · Microfone sempre aberto · Iniciar com o Windows · Sair) e abrir o programa de novo traz a janela que já estava rodando. Instalado pelo Setup, ele também abre sozinho no login, escondido.

Com `AlwaysListen`, o microfone fica aberto e o Jarvis só reage quando ouve a palavra de ativação, no começo ou no fim da frase: *"Jarvis, abre o Spotify"*, *"Abre o Spotify, Jarvis"*. Dizer só *"Jarvis"* faz ele responder "Pois não?" e a fala seguinte vale sem repetir o nome. Segurar **Espaço** continua funcionando. O ponto no canto inferior esquerdo do HUD mostra se o microfone está aberto; clicar nele (ou no menu da bandeja) liga/desliga.

Como funciona e o que isso significa para a privacidade:
- Um detector de voz simples (energia, sem rede) recorta cada fala; **nada é gravado em disco**, o áudio vive só na memória.
- Cada fala vai para o Whisper **local** do OpenJarvis. Se não tiver a palavra de ativação (e não estiver na janela de acompanhamento), o texto é descartado sem registro.
- Enquanto o Jarvis fala ou pensa o microfone é ignorado (para ele não se ouvir). Para interromper uma resposta, use **Espaço**.
- Cada fala detectada consome um pouco de CPU na transcrição. Em ambientes barulhentos (TV, muita conversa) reduza `ListenSensitivity`.
- Quando o app está escondido e baixa uma atualização, ele a instala sozinho assim que o Jarvis estiver quieto e reabre na bandeja.

## Instalador e atualizações automáticas

O app é distribuído com o [Velopack](https://velopack.io): um `Setup.exe` que instala por usuário (sem pedir administrador) em `%LOCALAPPDATA%\JarvisAssistantApp`, cria atalhos na Área de Trabalho e no Menu Iniciar e instala o WebView2 se faltar. Os dados (config, logs, voz clonada) ficam em `%APPDATA%` e `%LOCALAPPDATA%\JarvisAssistant`, separados do programa, então atualizar ou desinstalar não os apaga.

**Atualizações.** Ao abrir (após 45 s) e a cada 6 horas o app consulta os *Releases* do repositório, baixa a versão nova em segundo plano (só a diferença, quando possível) e mostra um aviso no topo do HUD. A instalação acontece ao fechar o app; o botão **Reiniciar e atualizar** faz isso na hora. Só vale para o app instalado: pelo `dotnet run` nada é verificado.

| Opção | Padrão | O que faz |
|---|---|---|
| `AutoUpdate` | `true` | Liga as atualizações automáticas |
| `UpdateRepo` | `https://github.com/TonyGuerra122/JarvisAssistant` | Repositório com os Releases (precisa ser público) |
| `UpdatePrerelease` | `false` | Aceita também versões de teste |
| `RunInBackground` | `true` | Fechar a janela (Alt+F4, Esc) esconde na bandeja e o Jarvis segue rodando; sair de verdade é pelo menu do ícone ou `Ctrl`+`Q` |
| `StartWithWindows` | `true` | Abre junto com o Windows, escondido na bandeja (só no app instalado) |
| `AlwaysListen` | `true` | Microfone sempre aberto: ele só age quando você diz a palavra de ativação |
| `WakeWord` | `"jarvis"` | Palavra de ativação (tolera pequenos erros de transcrição, como "Jarvi") |
| `FollowUpSeconds` | `8` | Depois que ele fala, você pode responder sem repetir o nome durante estes segundos (`0` desliga) |
| `VoiceConfirm` | `true` | Confirma ações dizendo "sim"/"não" (PowerShell livre exige Enter) |
| `ListenSensitivity` | `1` | Maior = pega voz mais baixa (e mais ruído de fundo) |

**Publicar uma versão** (uma vez: `dotnet tool install -g vpk`):

```powershell
git tag v1.0.1
git push origin v1.0.1      # o GitHub Actions (.github/workflows/release.yml) gera e publica tudo
```

Ou manualmente: `.\build\release.ps1 -Version 1.0.1` gera `Releases\JarvisAssistantApp-win-Setup.exe`; com `-Upload` (e `GITHUB_TOKEN` definido) também publica no GitHub. O número da versão vem da tag. O instalador não é assinado, então o Windows SmartScreen pode avisar na primeira instalação.

## Atalhos

| Tecla              | Ação                       |
| ------------------ | -------------------------- |
| `Espaço` (segurar) | Falar com o Jarvis         |
| `Enter`            | Digitar uma mensagem       |
| `Esc`              | Minimizar (ou fechar o campo de texto) |
| `Ctrl` + `Q`       | Sair                       |

## Configuração

As configurações ficam em `%APPDATA%\JarvisAssistant\config.json`, criado na primeira execução. Edite o arquivo e reabra o app. Opções novas são acrescentadas sem apagar as suas.

| Opção | Padrão | Descrição |
| --- | --- | --- |
| `UserTitle` | `"senhor"` | Como o Jarvis chama você |
| `City` | `"São Paulo"` | Cidade usada para o clima |
| `Language` | `"pt"` | Idioma da transcrição |
| `OpenJarvisUrl` | `http://127.0.0.1:8000` | Endereço do servidor OpenJarvis |
| `StartOpenJarvis` | `true` | Inicia `jarvis serve` se o servidor não estiver rodando |
| `StopOpenJarvisOnExit` | `true` | Encerra o servidor ao fechar, se foi o app que o iniciou |
| `AutoInstallOpenJarvis` | `true` | Instala o OpenJarvis e as dependências se faltarem |
| `OpenJarvisInstallBuildTools` | `true` | Permite instalar o VS Build Tools (pede UAC) |
| `OpenJarvisStarterModel` | `"qwen3.5:2b"` | Modelo baixado no Ollama se nenhum estiver definido |
| `OpenJarvisExtras` | `["desktop","voice"]` | Extras do OpenJarvis a instalar |
| `JarvisCommand` | `""` | Caminho do `jarvis.cmd` (vazio = `%LOCALAPPDATA%\OpenJarvis\bin\jarvis.cmd`) |
| `Agent` | `"simple"` | Agente do OpenJarvis usado no chat |
| `Model` | `""` | Modelo (vazio = o padrão do OpenJarvis) |
| `MaxTokens` | `350` | Tamanho máximo da resposta |
| `HistoryTurns` | `6` | Quantas trocas anteriores ele lembra |
| `Voice` | `"openjarvis"` | `openjarvis`, `clone` ou `windows` |
| `OpenJarvisVoiceId` | `"pm_alex"` | Voz do Kokoro |
| `CloneVoiceFile` | `""` | Áudio (mp3/wav) com a voz a clonar |
| `CloneVoicePort` | `8020` | Porta local do serviço de voz clonada |
| `VoiceSpeed` | `1.0` | Velocidade da fala |
| `VoicePitch` | `-0.5` | Tom em semitons (negativo = mais grave) |
| `VoiceEffect` | `0.7` | Efeito de voz de IA: `0` desligado, `1` sutil, `2` forte |
| `WindowsVoiceName` | `""` | Voz do Windows (vazio = primeira voz pt-BR) |
| `GreetOnStart` | `true` | Cumprimenta ao terminar de iniciar |
| `Personality` | *(mordomo britânico)* | Prompt de sistema; `{title}` vira o `UserTitle` |
| `Debug` | `false` | Liga o DevTools (`F12`) no HUD |
| `CommandsEnabled` | `true` | Liga as ações no PC (abrir programas, volume, temporizadores...) |
| `Programs` | *(vários)* | Programas que ele abre: nome falado → executável/URI. `"browser:"` = navegador padrão. Os que não estiverem aqui são procurados no Menu Iniciar (com confirmação) |
| `Routines` | *(2 exemplos)* | Rotinas suas: nome → comando do PowerShell |
| `ConfirmRoutines` | `true` | Confirma antes de rodar uma rotina |
| `AllowPowerShell` | `false` | Deixa o modelo escrever PowerShell livre (sempre com confirmação) |
| `ActionPolicies` | *(veja abaixo)* | Política por ação: `auto`, `confirmar` ou `bloquear` |
| `AllowedFolders` | *(pastas do usuário)* | Pastas onde ele pode procurar e abrir arquivos |

### Comandos de voz

Ações disponíveis: abrir programa, site e pesquisa; mídia (tocar/pausar, próxima, anterior); volume (definir, subir, descer, mudo); brilho; bloquear tela; desligar/reiniciar/cancelar; listar e fechar janelas; informações do sistema (bateria, CPU, memória, disco, rede); temporizadores, alarmes (com som, repetição por dia e soneca) e lembretes falados, salvos em `%LOCALAPPDATA%JarvisAssistanteminders.json`; área de transferência; abrir pasta e buscar arquivo (só em `AllowedFolders`); Spotify (tocar por busca, o que está tocando, volume do app, aleatório, repetir) e busca no YouTube; captura de tela (salva em `Imagens\Capturas`); encerrar o próprio Jarvis ("Jarvis, encerrar"; pede confirmação e aceita "sim"; política `encerrar_jarvis`); rotinas e, se liberado, PowerShell.

Em `ActionPolicies`, `"bloquear"` tira a ação das ferramentas do modelo e `"confirmar"` pergunta antes (Enter confirma, Esc cancela; sem resposta em 30 s conta como "não"). Desligar/reiniciar, fechar programa e PowerShell livre **sempre** confirmam. Por segurança, arquivos executáveis e scripts nunca são abertos por voz. Temporizadores, alarmes e lembretes sobrevivem ao fechamento do app; o que passar com ele fechado (até 12 h) é avisado na próxima abertura.

### Voz clonada

1. Defina `"Voice": "clone"` e `"CloneVoiceFile": "C:\\caminho\\para\\voz.mp3"`.
2. Reabra o app. Ele instala o serviço Python (XTTS-v2 via `coqui-tts`), limpa o áudio de referência (Demucs + redução de ruído) e baixa o modelo.

O serviço fica em `ServiceLibrary/VoiceClone/voice_server.py` e também pode ser usado sozinho:

```text
python voice_server.py prepare <audio.mp3|wav>   # gera reference.wav limpo
python voice_server.py download                  # baixa o modelo XTTS-v2
python voice_server.py serve --port 8020         # GET /health, POST /synthesize
```

> ⚠️ Use apenas a voz de quem autorizou. O modelo XTTS-v2 segue a [Coqui Public Model License](https://coqui.ai/cpml), que **permite somente uso não comercial**.

## Estrutura do projeto

```text
JarvisAssistant.slnx
├── GUI/                      App WPF (executável JarvisAssistant.exe)
│   ├── Views/MainWindow      Janela que hospeda o HUD no WebView2
│   └── web/                  HUD em HTML/CSS/JS (boot, legendas, clima, visualizador de áudio)
└── ServiceLibrary/           Lógica do assistente
    ├── Assistant.cs          Boot e ciclo ouvir → pensar → falar
    ├── AppConfig.cs          Leitura e gravação do config.json
    ├── OpenJarvisClient.cs   Cliente HTTP (chat, transcrição, síntese de voz)
    ├── OpenJarvisServer.cs   Inicia e encerra o "jarvis serve"
    ├── Audio/                Microfone, player, voz do Windows e efeito de voz (NAudio)
    ├── Setup/                Instalador automático, um passo por classe em Steps/
    └── VoiceClone/           Serviço Python de voz clonada (XTTS-v2)
```

### Como funciona

1. **Boot**: o app procura o OpenJarvis em `OpenJarvisUrl`. Se não o encontrar, instala e/ou inicia o servidor e depois verifica o modelo, a audição (STT), a voz (TTS) e o microfone.
2. **Ouvir**: o áudio gravado do microfone vai para `POST /v1/speech/transcribe`.
3. **Pensar**: o texto, o histórico e o prompt de personalidade vão para `POST /v1/chat/completions`. Blocos `<think>` e markdown são removidos da resposta.
4. **Falar**: a resposta é dividida em frases. Cada frase é sintetizada enquanto a anterior toca, para reduzir a latência.

## Arquivos e logs

| Local | Conteúdo |
| --- | --- |
| `%APPDATA%\JarvisAssistant\config.json` | Configurações |
| `%LOCALAPPDATA%\JarvisAssistant\jarvis.log` | Log do app |
| `%LOCALAPPDATA%\JarvisAssistant\openjarvis-serve.log` | Log do servidor OpenJarvis |
| `%LOCALAPPDATA%\OpenJarvis\` | Instalação do OpenJarvis |

## Solução de problemas

- **"Núcleo OpenJarvis offline"**: veja `openjarvis-serve.log`. Teste rodando `jarvis serve` manualmente.
- **"Módulo de audição indisponível"**: confirme que `OpenJarvisExtras` contém `"desktop"` e `"voice"` e reabra o app.
- **Sem voz**: o app tenta, em ordem, a voz clonada, o Kokoro e a voz do Windows. Se nenhuma funcionar, ele só mostra legendas.
- **Respostas lentas**: use um modelo menor em `Model` ou mantenha `Agent` como `"simple"`.

## Tecnologias

.NET 10 · WPF · WebView2 · NAudio · System.Speech · OpenJarvis · Ollama · Whisper · Kokoro · Coqui XTTS-v2
