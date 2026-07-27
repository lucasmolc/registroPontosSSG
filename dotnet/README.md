# Registro Automático de Pontos SSG — Desktop

Aplicativo Windows nativo (.NET + WPF) que automatiza o registro de pontos no
sistema SSG da Sysmap. Distribuído como um único `.exe` self-contained, sem
necessidade de instalar runtime ou dependências na máquina do usuário final.

## Para usuários finais (colegas de trabalho)

1. Receba o arquivo `RegistroPontosSSG.exe` da sua equipe
2. Salve em qualquer pasta (ex.: `Documentos\PontosSSG`)
3. Dê duplo-clique para abrir
4. Preencha as abas (Credenciais → Arquivo → Regras → Executar)
5. Clique em **🚀 Executar registro**

Sem instalação. Sem runtime. Sem editar arquivos de configuração à mão.

Veja [`docs/LEIA-ME.txt`](docs/LEIA-ME.txt) — instruções 1-página para distribuição.

## Para desenvolvedores

### Stack

- **.NET 10** + **WPF** (`net10.0-windows`)
- **CommunityToolkit.Mvvm** — MVVM com source generators
- **Microsoft.Playwright** — automação do navegador
- **ClosedXML** — leitura de arquivos Excel
- **Otp.NET** — geração de códigos TOTP (2FA)
- **ZXing.Net** — leitura de QR codes
- **System.Security.Cryptography.ProtectedData** — DPAPI para criptografar segredos

### Estrutura

```
RegistroPontosSSGDesktop/
├── RegistroPontosSSG.sln
└── src/
    ├── RegistroPontosSSG.Core/           # Lógica reutilizável (sem UI)
    │   ├── Models/                       # AppConfig, PunchRecord, ValidationRules
    │   ├── Security/                     # DPAPI, TOTP, QR
    │   ├── Configuration/                # ConfigService (carrega/salva %APPDATA%)
    │   ├── Reading/                      # PunchFileReader (Excel/CSV/SSG report)
    │   ├── Validation/                   # TimeValidator (ajustes de horário)
    │   └── Automation/                   # SsgAutomation (Playwright)
    └── RegistroPontosSSG.Desktop/        # Aplicação WPF
        ├── App.xaml + App.xaml.cs        # Resources, tema, exception handler
        ├── MainWindow.xaml + .cs         # 4 abas (Credenciais/Arquivo/Regras/Executar)
        ├── Views/TotpWizardWindow.xaml   # Wizard de configuração 2FA
        └── ViewModels/MainViewModel.cs   # Estado e comandos
```

### Build local

```powershell
cd c:\Projects\RegistroPontosSSGDesktop
dotnet build
dotnet run --project src\RegistroPontosSSG.Desktop
```

### Publicar como `.exe` único (para distribuição)

```powershell
dotnet publish src\RegistroPontosSSG.Desktop\RegistroPontosSSG.Desktop.csproj `
    -c Release -r win-x64 --self-contained true `
    -o publish
```

O executável final fica em `publish\RegistroPontosSSG.exe` (≈70–100 MB,
self-contained — não precisa de .NET instalado na máquina alvo).

### Configuração e dados do usuário

Todo estado fica em `%APPDATA%\RegistroPontosSSG\`:

| Arquivo / pasta      | Conteúdo                                         |
| -------------------- | ------------------------------------------------ |
| `config.json`        | Usuário, opções, regras (senha/2FA criptografados) |
| `logs/`              | Logs Serilog (rotação diária)                    |
| `browser_data/`      | Perfil persistente do Chromium (cookies, sessão) |

Senha e secret TOTP são criptografadas via Windows DPAPI no escopo
`CurrentUser` — só funcionam no mesmo usuário Windows que as salvou.

### Origem do projeto

Esta é a única implementação mantida. Ela nasceu como porte de um protótipo em
Python (removido do repositório para evitar manutenção duplicada) e cobre:

- Login + 2FA TOTP automático (`SsgAutomation.LoginAsync`)
- Detecção de planilhas exportadas pelo próprio SSG (`PunchFileReader`)
- Validações e ajustes de horário (`TimeValidator`)
- Filtro de mês atual/anterior e detecção de datas já registradas
- Preenchimento dos cards de dia e seleção de OSI/Projeto/Atividade

Os seletores da SPA do SSG estão centralizados em `SsgSelectors.cs`; a tabela
de referência e as armadilhas conhecidas (campos mascarados, preset de período
obrigatório) estão documentadas no [README raiz](../README.md).

## Licença

MIT — veja [`LICENSE`](LICENSE).
