# Sistema de Registro Automático de Pontos SSG

Automação para leitura de arquivos de pontos (Excel/CSV) e registro automático no sistema SSG da Sysmap.

Aplicativo Windows nativo em **.NET 10 + WPF**, distribuído como `.exe` único self-contained
(~96 MB — não requer .NET instalado na máquina do usuário).

> 🔒 **Segurança:** o `.gitignore` impede o commit de credenciais (`%APPDATA%\RegistroPontosSSG\config.json`),
> arquivos pessoais de pontos (`*.xlsx`), QR codes e secrets TOTP. Senha e secret de 2FA são
> armazenados criptografados via DPAPI.

## Início rápido

```powershell
dotnet build
dotnet run --project src\RegistroPontosSSG.Desktop
```

Publicar `.exe` único:

```powershell
dotnet publish src\RegistroPontosSSG.Desktop\RegistroPontosSSG.Desktop.csproj `
    -c Release -r win-x64 --self-contained true -o publish
```

Documentação detalhada (design system, arquitetura MVVM, DPAPI, distribuição) em
[`docs/desenvolvimento.md`](./docs/desenvolvimento.md) e [`docs/design-system.md`](./docs/design-system.md).

## Estrutura do repositório

```
registroPontosSSG/
├── RegistroPontosSSG.sln
├── src/
│   ├── RegistroPontosSSG.Core/          # Lógica reutilizável, sem UI
│   │   ├── Automation/                  # SsgAutomation + SsgSelectors (Playwright)
│   │   ├── Configuration/               # ConfigService (%APPDATA%)
│   │   ├── Models/                      # AppConfig, PunchRecord, ValidationRules
│   │   ├── Reading/                     # PunchFileReader (Excel/CSV/relatório SSG)
│   │   ├── Security/                    # DPAPI, TOTP, leitura de QR code
│   │   └── Validation/                  # TimeValidator (ajustes de horário)
│   └── RegistroPontosSSG.Desktop/       # Aplicação WPF
│       ├── Assets/                      # Ícone do aplicativo
│       ├── ViewModels/                  # MainViewModel
│       ├── Views/                       # Wizard de 2FA, log verboso
│       └── app.manifest                 # DPI awareness e nível de privilégio
└── docs/
    ├── desenvolvimento.md               # Stack, build, publicação, dados do usuário
    ├── design-system.md                 # Tokens visuais e componentes
    └── LEIA-ME.txt                      # Instruções de 1 página para o usuário final
```

Diretórios gerados em tempo de execução ou build (`bin/`, `obj/`, `publish/`, `logs/`,
`browser_data/`) não são versionados.

## Funcionalidades

- Leitura de pontos de arquivos Excel (`.xlsx`) ou CSV, incluindo relatórios exportados do próprio SSG
- Detecção de datas já cadastradas (evita duplicatas)
- Ajuste automático de horários conforme regras do SSG, com compensação para preservar o total de horas
- Usa o Chrome do sistema para passar na verificação Cloudflare — e reaproveita uma instância de
  debug já aberta, se houver
- Preenchimento automático de Entrada/Saída com múltiplos pares por dia
- Seleção automática de OSI/Projeto/Atividade
- 2FA via TOTP (opcional), com wizard de leitura do QR code
- Log verboso por execução em `logs/run-<timestamp>.log`

## Requisitos

| Requisito | Descrição |
| --------- | --------- |
| Sistema Operacional | Windows 10 ou superior (64 bits) |
| .NET | **não** é necessário no `.exe` self-contained; SDK 10 para desenvolver |
| Navegador | Google Chrome instalado (recomendado, para o Cloudflare) |
| Internet | Acesso ao SSG e ao portal Sysmap |

## Arquivo de pontos

Planilha com as colunas:

| data       | entrada | saida_almoco | retorno_almoco | saida | observacao |
| ---------- | ------- | ------------ | -------------- | ----- | ---------- |
| 01/07/2026 | 08:00   | 12:00        | 13:00          | 17:00 |            |
| 02/07/2026 | 08:30   | 12:00        | 13:00          | 17:30 | Home Office |

Relatórios exportados do SSG também são aceitos (formato detectado automaticamente).

## Fluxo de execução

1. Lê o arquivo de pontos e exibe os registros
2. Abre o Chrome (reutilizando uma sessão de debug existente, se houver) e faz login no SSG,
   preenchendo o 2FA automaticamente quando o TOTP está configurado
3. Abre **Registros de Entrada/Saída** (`#/access-entry/get-list`) e filtra o período pelo preset
   "Mês Atual" / "Mês Anterior"
4. Varre os cards de dia para descobrir o que já está cadastrado
5. Para cada data pendente: preenche os pares Entrada/Saída, as horas apontadas e o OSI/Projeto
6. Clica em **Salvar dias alterados** e reporta a mensagem devolvida pelo SSG
7. Mantém o navegador aberto para validação manual

## Interface do SSG (referência técnica)

O SSG migrou de páginas ASP clássicas para uma SPA AngularJS. A tela antiga
`/new/timesheet/timesheetrecording.asp` (tabela `#TableTimesheet`) apenas redireciona para
`index.html#/access-entry/get-list`, onde **os cards de todos os dias do período já vêm
renderizados** — não é preciso criar linha nem digitar a data.

Os seletores usados pela automação estão centralizados em
[`SsgSelectors.cs`](./src/RegistroPontosSSG.Core/Automation/SsgSelectors.cs):

| Elemento | Seletor |
| -------- | ------- |
| Preset de período | `.date-range-component a.button-current-month` / `a.button-previous-month` |
| Filtrar | `button.button-filter` |
| Card do dia | `.access-entry-day[data-date="DD/MM/YYYY"]` |
| Expandir/colapsar dia | `button.button-toggle-day` |
| Adicionar linha de E/S | `button.button-add-access-row` |
| Entrada / Saída | `input.input-clock-in` / `input.input-clock-out` |
| Horas totais do dia (SSG) | `.access-total-hours` |
| Adicionar apontamento | `button.button-add-appointment-row` |
| Horas apontadas | `input.input-appointed-hours` |
| OSI / Projeto / Atividade | `input.input-project-activity` |
| Listagem de itens (OSI) | `button.button-show-items` → `.modal-list-items` → `button.button-select` |
| Salvar | `button.button-save-access-entry` |

Atributos relevantes do card: `data-date`, `data-day-status`, `data-access-entry-allowed`,
`data-is-valid-date`. Dias com `data-access-entry-allowed="N"` ou `data-is-valid-date="N"` não
aceitam lançamento e são reportados no log.

Dois detalhes que quebram a automação se ignorados:

- Campos `.mask-time` **ignoram `FillAsync`** (o valor fica vazio ou `__:__`). É preciso enviar
  apenas os 4 dígitos e deixar a máscara aplicar o `:`.
- O filtro **exige o preset de período**: digitar as datas nos campos mascarados faz o Angular
  responder "O campo Período é de preenchimento obrigatório".

## Regras de validação automática

Os horários são ajustados **mantendo o total de horas trabalhadas igual ao original**: quando um
horário precisa mudar, o sistema compensa em outro.

| Situação | Ajuste | Compensação |
| -------- | ------ | ----------- |
| Entrada redonda | 08:00 → 08:01 | Saída +1min |
| Saída para almoço redonda | 12:00 → 12:01 | Saída -1min |
| Retorno do almoço redondo | 13:00 → 13:01 | Saída +1min |
| Almoço de 1h exata | 12:00-13:00 → 12:00-13:01 | Saída +1min |
| Horário duplicado em dias próximos | +1min até não duplicar | Saída compensada |

Exemplo: `08:00 - 12:00 | 13:00 - 17:00` (8h) vira `08:01 - 12:00 | 13:01 - 17:02` — ainda 8h.

As regras são configuráveis na aba **Regras** do aplicativo.

## 2FA automático (opcional)

Com a **secret key** do TOTP configurada, o código de 2FA é preenchido automaticamente no login.
Sem ela, o app aguarda você digitar o código no navegador.

Para obter a secret key é necessário reconfigurar o 2FA no portal Sysmap (abra um chamado
solicitando a troca de dispositivo). Durante a reconfiguração é exibido um QR code — use o
**wizard de 2FA** do aplicativo para lê-lo a partir de um print da tela.

> ⚠️ Antes de prosseguir, escaneie o QR code também no seu app Authenticator, garantindo backup
> para login manual. Depois de configurar, apague a imagem do QR code.

A secret é gravada criptografada via DPAPI (só o seu usuário Windows consegue decifrar).

## Solução de problemas

| Problema | Causa provável / correção |
| -------- | ------------------------- |
| Cloudflare não passa | Use o Chrome do sistema (opção ativa por padrão) |
| Erro `ECONNREFUSED ::1:<porta>` | Conexão CDP deve usar `127.0.0.1`, não `localhost` — corrigido |
| Chrome abre e a automação não conecta | Já existe um Chrome usando o mesmo perfil; o app detecta e reutiliza a instância de debug |
| Horários não preenchem | Campos mascarados exigem digitação dígito a dígito |
| "O campo Período é obrigatório" | O filtro precisa do preset de período, não das datas digitadas |
| Dia não aceita lançamento | Card com `data-access-entry-allowed="N"` — status/período bloqueado no SSG |

## Licença

MIT License
