# Sistema de Registro Automático de Pontos SSG

Automação para leitura de arquivos de pontos (Excel/CSV) e registro automático no sistema SSG da Sysmap.

O repositório contém **duas implementações** equivalentes da mesma automação:

| Implementação | Pasta | Stack | Distribuição |
| ------------- | ----- | ----- | ------------ |
| **Python (CLI)** | raiz do repo (`main.py`, `src/`) | Python 3.10+ · Playwright · pandas | Script ou `.exe` via PyInstaller |
| **Desktop (.NET / WPF)** | [`dotnet/`](./dotnet/) | .NET 10 · WPF · Playwright .NET | `.exe` self-contained (single-file) |

Ambas executam o mesmo fluxo (login → 2FA → leitura do arquivo → validação → registro no SSG). Escolha:

- **Python** — se você é desenvolvedor, quer rodar via terminal ou customizar regras
- **.NET Desktop** — para distribuir um `.exe` único aos usuários finais (sem instalar Python)

> 🔒 **Segurança:** o `.gitignore` impede o commit de credenciais (`config/config.yaml`, `%APPDATA%\RegistroPontosSSG\config.json`), arquivos pessoais de pontos (`data/pontos/*.xlsx`), QR codes, secrets TOTP e binários (`bin/`, `obj/`, `publish/`, `*.zip`).

## 🖥️ Versão Desktop (.NET / WPF)

Aplicativo Windows nativo com interface moderna em tema escuro, distribuído como `.exe` único self-contained (~96 MB — não requer .NET instalado na máquina do usuário).

```powershell
cd dotnet
dotnet build
dotnet run --project src\RegistroPontosSSG.Desktop
```

Publicar `.exe` único:

```powershell
cd dotnet
dotnet publish src\RegistroPontosSSG.Desktop\RegistroPontosSSG.Desktop.csproj `
    -c Release -r win-x64 --self-contained true -o publish
```

Detalhes completos (design system, arquitetura MVVM, DPAPI, etc.) em [`dotnet/README.md`](./dotnet/README.md) e [`dotnet/docs/design-system.md`](./dotnet/docs/design-system.md).

---

## 🐍 Versão Python (CLI)

## ✨ Funcionalidades

- ✅ Leitura de pontos de arquivos Excel (.xlsx) ou CSV
- ✅ Suporte a relatórios exportados do SSG
- ✅ Detecção automática de datas já cadastradas (evita duplicatas)
- ✅ Ajuste automático de horários conforme regras do SSG
- ✅ Usa Chrome do sistema para passar na verificação Cloudflare
- ✅ Preenchimento automático de E-S (Entrada-Saída) com múltiplos registros
- ✅ Seleção automática de projeto/OSI
- ✅ Suporte a 2FA via TOTP (opcional)
- ✅ Compensação automática de horas ao ajustar horários
- ✅ Geração de executável (.exe) para distribuição
- ✅ Criação automática de ZIP para compartilhamento

## 📁 Estrutura do Projeto

```
registroPontosSSG/
├── config/
│   ├── __init__.py
│   ├── config.yaml         # Configurações do sistema
│   └── settings.py         # Classe de configurações
├── data/
│   └── pontos/
│       └── pontos.xlsx     # Arquivo de pontos
├── logs/
│   └── registro_pontos.log # Logs de execução
├── src/
│   ├── __init__.py
│   ├── automacao_ssg.py    # Automação web com Playwright
│   ├── leitor_pontos.py    # Leitura de arquivos de pontos
│   ├── validador_horarios.py # Validação e ajuste de horários
│   └── logger_config.py    # Configuração de logs
├── browser_data/           # Dados do navegador (sessão)
├── bootstrap.py            # Configuração automática do ambiente
├── build_exe.py            # Script para gerar executável
├── decode_qr.py            # Extração de secret key do QR code
├── main.py                 # Ponto de entrada
├── README.md
└── requirements.txt
```

## � Requisitos do Sistema

### Para usar o Executável (.exe)

| Requisito | Descrição |
|-----------|------------|
| Sistema Operacional | Windows 10 ou superior (64 bits) |
| Python | **NÃO é necessário** ter Python instalado |
| Navegador | Google Chrome instalado (recomendado) |
| Internet | Conexão com internet para acessar o SSG |
| Espaço em disco | ~500 MB (para o navegador Playwright na primeira execução) |

> **Nota:** Na primeira execução, o sistema baixará automaticamente o navegador Chromium do Playwright (~150 MB). Isso acontece apenas uma vez.

### Para usar com Python (desenvolvimento)

| Requisito | Versão |
|-----------|--------|
| Python | 3.10 ou superior (testado com 3.11) |
| pip | Incluído no Python |
| Sistema Operacional | Windows, Linux ou macOS |

**Dependências principais:**
- playwright >= 1.40.0
- pandas >= 2.0.0
- openpyxl >= 3.1.0
- pyyaml >= 6.0
- loguru >= 0.7.0
- pyotp >= 2.9.0 (opcional, para 2FA automático)

## 🚀 Instalação

### Opção 1: Executável (Recomendado para usuários)

1. Baixe o arquivo `RegistroPontosSSG.zip`
2. Extraia para uma pasta de sua preferência
3. Siga as instruções no arquivo `LEIA-ME.txt`

### Opção 2: Python (Para desenvolvedores)

1. Clone o repositório ou acesse a pasta do projeto:
```bash
cd registroPontosSSG
```

2. Execute o sistema (o ambiente é configurado automaticamente):
```bash
python main.py
```

O sistema irá automaticamente:
- Criar ambiente virtual se não existir
- Instalar dependências
- Instalar navegadores do Playwright

### Instalação Manual (opcional)

```bash
# Criar ambiente virtual
python -m venv venv
venv\Scripts\activate  # Windows

# Instalar dependências
pip install -r requirements.txt

# Instalar navegadores
playwright install chromium
```

## ⚙️ Configuração

### 1. Arquivo de Configuração

Edite o arquivo `config/config.yaml`:

```yaml
# Configurações do Sistema SSG
ssg:
  url: "https://ssg.sysmap.com.br"
  timesheet_url: "https://ssg.sysmap.com.br/new/timesheet/timesheetrecording.asp"

# Credenciais de Login
credentials:
  username: "seu.usuario"
  password: "sua_senha"
  # Secret key do TOTP para 2FA automático (opcional)
  # totp_secret: "SUASECRETKEYAQUI"

# Configurações de Arquivo de Pontos
arquivo_pontos:
  diretorio: "data/pontos"
  nome_arquivo: "pontos.xlsx"
  formato: "xlsx"

# Regras de Validação (ajustes automáticos)
validacao:
  bloquear_horarios_redondos: true      # 08:00 -> 08:01
  dias_verificar_duplicados: 5
  bloquear_horarios_duplicados: true     # Evita horários repetidos
  bloquear_almoco_1_hora_exata: true     # Almoço 1h -> 1h01

# Configurações de Automação
automacao:
  timeout: 30000
  headless: false
  slow_mo: 100
  selecionar_mes_atual: true
  ignorar_datas_existentes: true
  # Recomendado para passar no Cloudflare
  usar_chrome_sistema: true
  chrome_path: ""  # Deixe vazio para detecção automática
  usar_perfil_chrome: false
```

### 2. Variáveis de Ambiente (Opcional)

Para maior segurança, use variáveis de ambiente:
```bash
set SSG_USERNAME=seu.usuario
set SSG_PASSWORD=sua_senha
```

### 3. Arquivo de Pontos

Crie um arquivo Excel (`data/pontos/pontos.xlsx`) com as colunas:

| data       | entrada | saida_almoco | retorno_almoco | saida | observacao |
|------------|---------|--------------|----------------|-------|------------|
| 01/01/2026 | 08:00   | 12:00        | 13:00          | 17:00 |            |
| 02/01/2026 | 08:30   | 12:00        | 13:00          | 17:30 | Home Office|

**Ou** use um relatório exportado do próprio SSG (formato detectado automaticamente).

## 📝 Uso

```bash
python main.py
```

### Fluxo de Execução

1. 📂 Lê o arquivo de pontos configurado
2. 📋 Exibe os registros encontrados
3. 🔐 Realiza login no SSG (aguarda 2FA se necessário)
4. 📆 Filtra pelo mês atual
5. 🔍 Detecta datas já cadastradas
6. ⏸️ Aguarda confirmação para continuar
7. 📝 Registra cada ponto automaticamente:
   - Preenche data
   - Preenche horários de E-S (entrada/saída)
   - Seleciona "ATIVIDADE EXTERNA"
   - Preenche horas trabalhadas
   - Seleciona projeto/OSI
8. 💾 Confirma todos os apontamentos
9. ⏸️ Mantém navegador aberto para validação

## 🔒 2FA Automático (Opcional)

Se você tiver a **secret key** do TOTP, pode automatizar o preenchimento do código 2FA no login.

### Pré-requisito: Trocar Dispositivo de 2FA

Para obter a secret key, você precisa **reconfigurar o 2FA** no portal Sysmap:

1. **Abra um chamado** no suporte da Sysmap solicitando a **troca de dispositivo de 2FA**
2. Aguarde a aprovação e siga as instruções do suporte
3. Durante a reconfiguração, será exibido um **QR code** na tela
4. **Tire um print/screenshot** da tela com o QR code e salve como imagem (ex: `qr.png` ou `qr.jpeg`)

> ⚠️ **IMPORTANTE:** Antes de prosseguir, **escaneie o QR code no seu aplicativo Authenticator** (Microsoft Authenticator, Google Authenticator, etc.). Isso garante que você tenha um backup para login manual caso necessário.

### Passo 1: Extrair a Secret Key do QR Code

A secret key está embutida no QR code. Existem várias formas de extraí-la:

#### Opção A: Sites de Leitura de QR Code (Recomendado)

Use um site para ler o conteúdo do QR code:

| Site | URL |
|------|-----|
| WebQR | https://webqr.com |
| QR Code Reader | https://qrcodescan.in |
| 4QRCode | https://4qrcode.com/scan-qr-code.php |
| ZXing Decoder | https://zxing.org/w/decode.jspx |

**Passo a passo:**
1. Acesse um dos sites acima
2. Faça upload da imagem do QR code
3. O site exibirá uma URL no formato:
   ```
   otpauth://totp/SysMap:seu.usuario@sysmap.com.br?secret=SUASECRETKEYAQUI&issuer=SysMap
   ```
4. Copie o valor após `secret=` (até o próximo `&`)
   - Exemplo: se a URL tem `secret=ABC123XYZ&issuer=`, a secret key é `ABC123XYZ`

#### Opção B: Aplicativos de Celular

Alguns apps de Authenticator permitem exportar a secret key:

1. **Aegis Authenticator** (Android - Open Source)
   - Escaneie o QR code
   - Toque e segure na conta adicionada
   - Selecione "Editar"
   - A secret key será exibida

2. **2FAS** (Android/iOS)
   - Escaneie o QR code
   - Acesse as configurações da conta
   - Opção "Exibir chave secreta"

#### Opção C: Extensão do Navegador

1. Instale a extensão **Authenticator** (disponível para Chrome/Firefox/Edge)
   - Chrome: https://chrome.google.com/webstore/detail/authenticator/bhghoamapcdpbohphigoooaddinpkbai
2. Ao escanear o QR code, a extensão mostra a secret key automaticamente

### Passo 2: Configurar no config.yaml

Adicione a secret key no arquivo de configuração:

```yaml
credentials:
  username: "seu.usuario"
  password: "sua_senha"
  totp_secret: "SUASECRETKEYAQUI"  # Secret key extraída do QR code
```

### Passo 3: Apagar Imagem do QR Code

Após configurar a secret key:
1. **Apague a imagem do QR code** do seu computador
2. **Limpe o histórico** dos sites usados para decodificar (se aplicável)
3. Nunca compartilhe ou envie o QR code para ninguém
4. Certifique-se de que o código funciona no Authenticator antes de apagar

### Como funciona

| Configuração | Comportamento |
|--------------|---------------|
| `totp_secret` configurado | 2FA preenchido **automaticamente** |
| `totp_secret` não configurado | Aguarda preenchimento **manual** |

O sistema gera o código TOTP usando a mesma lógica do Microsoft Authenticator, então os códigos são idênticos.

### ⚠️ Segurança

- **Armazenar a secret key** no computador reduz a segurança do 2FA
- Mantenha o arquivo `config.yaml` **protegido** e nunca o envie para ninguém
- **Sempre mantenha o Authenticator** configurado como backup para login manual
- Ao usar sites online, prefira os que processam localmente (WebQR processa no navegador)
- Se suspeitar de comprometimento, solicite nova troca de dispositivo 2FA

## 🔧 Regras de Validação Automática

O sistema ajusta automaticamente os horários **mantendo o total de horas trabalhadas igual ao original**.

### Como funciona a compensação

Quando um horário precisa ser ajustado (por ser redondo ou duplicado), o sistema **compensa automaticamente** em outro horário para manter o total de horas:

| Situação | Ajuste | Compensação |
|----------|--------|-------------|
| Entrada redonda | 08:00 → 08:01 | Saída +1min (17:00 → 17:01) |
| Saída almoço redonda | 12:00 → 12:01 | Saída -1min (compensa perda) |
| Retorno almoço redondo | 13:00 → 13:01 | Saída +1min (17:00 → 17:01) |
| Almoço 1h exata | 12:00-13:00 → 12:00-13:01 | Saída +1min |
| Horário duplicado | +1min até não duplicar | Saída compensada |

### Exemplo prático

**Original:** 08:00 - 12:00 | 13:00 - 17:00 = **8h trabalhadas**

**Ajustes aplicados:**
- Entrada: 08:00 → 08:01 (redondo)
- Retorno: 13:00 → 13:01 (redondo)
- Saída: 17:00 → 17:02 (compensação +2min)

**Final:** 08:01 - 12:00 | 13:01 - 17:02 = **8h trabalhadas** ✅

### Regras aplicadas

| Regra | Descrição | Config |
|-------|-----------|--------|
| Horário redondo | Horários com :00 recebem +1min | `bloquear_horarios_redondos` |
| Almoço 1h exata | Almoço de 60min vira 61min | `bloquear_almoco_1_hora_exata` |
| Horário duplicado | Evita repetir horários nos últimos X dias | `bloquear_horarios_duplicados` |

## 📋 Dependências

- **playwright** - Automação de navegadores
- **playwright-stealth** - Bypass de detecção de automação
- **pandas** - Manipulação de dados
- **openpyxl** - Leitura de arquivos Excel
- **python-dotenv** - Variáveis de ambiente
- **pyyaml** - Leitura de arquivos YAML
- **loguru** - Sistema de logs
- **pyotp** - Geração de códigos TOTP (opcional)

## 🐛 Solução de Problemas

### Cloudflare não passa
- Use `usar_chrome_sistema: true` no config
- Feche todas as janelas do Chrome antes de executar

### Data não preenche
- O sistema usa `type()` caractere por caractere
- Verifique o formato da data (DD/MM/YYYY)

### Horários não preenchem
- Horários são formatados como HH:MM (ex: 08:00)
- Verifique se o arquivo de pontos está correto

### Navegador fecha antes de validar
- O sistema aguarda ENTER antes de fechar
- Verifique se há erros no log

## � Geração de Executável

Para gerar um executável distribuível:

```bash
python build_exe.py
```

O script irá:
1. Gerar `RegistroPontosSSG.exe` na pasta `dist/`
2. Criar estrutura de distribuição com pastas necessárias
3. Copiar arquivos de configuração e documentação
4. Criar arquivo `RegistroPontosSSG.zip` pronto para compartilhar

### Conteúdo do ZIP

```
RegistroPontosSSG/
├── RegistroPontosSSG.exe    # Executável
├── config/
│   └── config.example.yaml  # Template de configuração
├── data/
│   └── pontos/              # Colocar pontos.xlsx aqui
├── logs/                    # Logs de execução
├── LEIA-ME.txt              # Instruções rápidas
└── README.md                # Documentação completa
```

### Primeira Execução do Executável

Na primeira execução, o Playwright precisará baixar o navegador Chrome (~150MB).
Isso é automático e acontece apenas uma vez.

## �📄 Licença

MIT License