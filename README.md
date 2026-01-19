# Sistema de Registro Automático de Pontos SSG

Sistema em Python para leitura automática de arquivos de pontos (Excel/CSV) e registro automático no sistema SSG da Sysmap.

## ✨ Funcionalidades

- ✅ Leitura de pontos de arquivos Excel (.xlsx) ou CSV
- ✅ Suporte a relatórios exportados do SSG
- ✅ Detecção automática de datas já cadastradas (evita duplicatas)
- ✅ Ajuste automático de horários conforme regras do SSG
- ✅ Usa Chrome do sistema para passar na verificação Cloudflare
- ✅ Preenchimento automático de E-S (Entrada-Saída) com múltiplos registros
- ✅ Seleção automática de projeto/OSI
- ✅ Suporte a 2FA via TOTP (opcional)

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
├── main.py                 # Ponto de entrada
├── README.md
└── requirements.txt
```

## 🚀 Instalação

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

Se você tiver a **secret key** do TOTP, pode automatizar o 2FA.

### Passo 1: Obter a Secret Key

A secret key está embutida no QR code do 2FA. Para extraí-la:

1. Reconfigure o 2FA no portal Sysmap para obter um novo QR code
2. Salve a imagem do QR code como `qr.jpeg` na pasta do projeto
3. Execute o script de decodificação:
   ```bash
   # Windows (usando venv)
   .\venv\Scripts\python.exe decode_qr.py qr.jpeg
   
   # Ou com Python global
   python decode_qr.py qr.jpeg
   ```
4. O script mostrará a secret key:
   ```
   === Informações extraídas ===
   Secret Key: SUASECRETKEYAQUI
   Issuer: SysMap
   ```
5. **Importante:** Escaneie o QR code no Microsoft Authenticator também (backup)
6. Apague a imagem do QR code após extrair a secret key

### Passo 2: Configurar no config.yaml

Adicione a secret key no arquivo de configuração:
```yaml
credentials:
  username: "seu.usuario"
  password: "sua_senha"
  totp_secret: "SUASECRETKEYAQUI"
```

### Passo 3: Instalar dependência

```bash
pip install pyotp
```

### Como funciona

- Se `totp_secret` estiver configurado → 2FA é preenchido **automaticamente**
- Se não estiver → aguarda preenchimento manual (como antes)

⚠️ **Segurança**: Armazenar a secret key no computador reduz a segurança do 2FA. Mantenha o arquivo `config.yaml` protegido e nunca o envie para repositórios públicos.

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

## 📄 Licença

MIT License