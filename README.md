# Sistema de Registro Automático de Pontos SSG

Sistema em Python para leitura automática de arquivos de pontos (Excel/CSV) e registro automático no sistema SSG.

## 📁 Estrutura do Projeto

```
registroPontosSSG/
├── config/
│   ├── __init__.py
│   ├── config.yaml         # Configurações do sistema
│   └── settings.py         # Classe de configurações
├── data/
│   └── pontos.xlsx         # Arquivo de pontos (criar)
├── logs/
│   └── registro_pontos.log # Logs de execução
├── src/
│   ├── __init__.py
│   ├── automacao_ssg.py    # Automação web com Playwright
│   ├── leitor_pontos.py    # Leitura de arquivos de pontos
│   └── logger_config.py    # Configuração de logs
├── .env.example            # Exemplo de variáveis de ambiente
├── .gitignore
├── main.py                 # Ponto de entrada
├── README.md
└── requirements.txt
```

## 🚀 Instalação

1. Clone o repositório ou acesse a pasta do projeto:
```bash
cd registroPontosSSG
```

2. Crie um ambiente virtual:
```bash
python -m venv venv
venv\Scripts\activate  # Windows
```

3. Instale as dependências:
```bash
pip install -r requirements.txt
```

4. Instale os navegadores do Playwright:
```bash
playwright install chromium
```

## ⚙️ Configuração

### 1. Arquivo de Configuração

Edite o arquivo `config/config.yaml` com as configurações do SSG:

```yaml
ssg:
  url: "https://ssg.exemplo.com.br"
  login_url: "https://ssg.exemplo.com.br/login"

credentials:
  username: "seu_usuario"
  password: "sua_senha"
```

### 2. Variáveis de Ambiente (Opcional)

Copie `.env.example` para `.env` e configure:
```bash
SSG_USERNAME=seu_usuario
SSG_PASSWORD=sua_senha
```

### 3. Arquivo de Pontos

Crie um arquivo Excel (`data/pontos.xlsx`) com as seguintes colunas:

| data       | entrada | saida_almoco | retorno_almoco | saida | observacao |
|------------|---------|--------------|----------------|-------|------------|
| 01/01/2026 | 08:00   | 12:00        | 13:00          | 17:00 |            |
| 02/01/2026 | 08:30   | 12:00        | 13:00          | 17:30 | Home Office|

## 📝 Uso

Execute o sistema:
```bash
python main.py
```

O sistema irá:
1. Ler o arquivo de pontos configurado
2. Exibir os registros encontrados
3. Solicitar confirmação antes de prosseguir
4. Realizar login no SSG
5. Registrar cada ponto automaticamente

## 🔧 Personalização

### Ajustar Seletores da Página

O arquivo `src/automacao_ssg.py` contém métodos com seletores genéricos marcados com `TODO`. 
Você precisa ajustá-los conforme a estrutura real da página do SSG:

- `fazer_login()` - Seletores do formulário de login
- `navegar_para_registro_ponto()` - Navegação até a página de registro
- `registrar_ponto()` - Campos do formulário de registro

## 📋 Dependências

- **playwright** - Automação de navegadores
- **pandas** - Manipulação de dados
- **openpyxl** - Leitura de arquivos Excel
- **python-dotenv** - Variáveis de ambiente
- **pyyaml** - Leitura de arquivos YAML
- **loguru** - Sistema de logs

## 📄 Licença

MIT License