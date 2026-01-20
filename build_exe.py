"""
Script para gerar o executável do Sistema de Registro de Pontos SSG.

Uso:
    python build_exe.py

O executável será gerado na pasta 'dist/'.
Um arquivo ZIP para distribuição será criado em 'dist/RegistroPontosSSG.zip'.
"""
import os
import sys
import shutil
import subprocess
import zipfile
from pathlib import Path
from datetime import datetime


def build():
    """Gera o executável usando PyInstaller."""
    
    print("🔧 Gerando executável do Sistema de Registro de Pontos SSG...\n")
    
    # Diretório base
    base_dir = Path(__file__).parent
    
    # Verifica se PyInstaller está instalado
    try:
        import PyInstaller
        print(f"✅ PyInstaller encontrado: {PyInstaller.__version__}")
    except ImportError:
        print("❌ PyInstaller não encontrado. Instalando...")
        subprocess.run([sys.executable, "-m", "pip", "install", "pyinstaller"], check=True)
        print("✅ PyInstaller instalado")
    
    # Verifica se existe ícone
    icon_path = base_dir / "assets" / "icon.ico"
    icon_arg = f'--icon="{icon_path}"' if icon_path.exists() else ""
    
    if icon_path.exists():
        print(f"✅ Ícone encontrado: {icon_path}")
    else:
        print("⚠️  Ícone não encontrado. Para adicionar um ícone:")
        print("   1. Crie a pasta 'assets/'")
        print("   2. Adicione um arquivo 'icon.ico'")
        print("   3. Execute novamente o build\n")
    
    # Comando PyInstaller
    cmd = [
        sys.executable, "-m", "PyInstaller",
        "--name=RegistroPontosSSG",
        "--onefile",
        "--console",  # Mostra console para ver logs
        "--clean",
        # Adiciona arquivos de dados
        f"--add-data={base_dir / 'config' / 'config.example.yaml'};config",
        # Imports ocultos necessários
        "--hidden-import=playwright",
        "--hidden-import=playwright.sync_api",
        "--hidden-import=pandas",
        "--hidden-import=openpyxl",
        "--hidden-import=yaml",
        "--hidden-import=loguru",
        "--hidden-import=pyotp",
        # Coleta todos os dados do playwright
        "--collect-all=playwright",
        "--collect-all=playwright_stealth",
    ]
    
    # Adiciona ícone se existir
    if icon_path.exists():
        cmd.append(f"--icon={icon_path}")
    
    # Arquivo principal
    cmd.append(str(base_dir / "main.py"))
    
    print("🚀 Executando PyInstaller...\n")
    print(f"   Comando: {' '.join(cmd)}\n")
    
    # Executa PyInstaller
    result = subprocess.run(cmd, cwd=str(base_dir))
    
    if result.returncode == 0:
        print("\n" + "=" * 50)
        print("✅ Executável gerado com sucesso!")
        print("=" * 50)
        print(f"\n📁 Localização: {base_dir / 'dist' / 'RegistroPontosSSG.exe'}")
        print("\n📋 Instruções de uso:")
        print("   1. Copie a pasta 'dist/RegistroPontosSSG.exe' para onde desejar")
        print("   2. Crie uma pasta 'config/' ao lado do executável")
        print("   3. Copie 'config.example.yaml' para 'config/config.yaml'")
        print("   4. Edite 'config/config.yaml' com suas credenciais")
        print("   5. Crie uma pasta 'data/pontos/' e adicione seu arquivo de pontos")
        print("   6. Execute 'RegistroPontosSSG.exe'")
        print("\n⚠️  IMPORTANTE:")
        print("   - Na primeira execução, o Playwright vai baixar o navegador")
        print("   - Isso pode demorar alguns minutos")
        
        # Cria estrutura de distribuição e ZIP
        criar_estrutura_dist(base_dir)
        criar_zip_distribuicao(base_dir)
    else:
        print("\n❌ Erro ao gerar executável")
        sys.exit(1)


def criar_estrutura_dist(base_dir: Path):
    """Cria estrutura de pastas para distribuição."""
    
    dist_dir = base_dir / "dist"
    
    # Cria pastas necessárias
    (dist_dir / "config").mkdir(exist_ok=True)
    (dist_dir / "data" / "pontos").mkdir(parents=True, exist_ok=True)
    (dist_dir / "logs").mkdir(exist_ok=True)
    
    # Copia config.example.yaml
    config_example = base_dir / "config" / "config.example.yaml"
    if config_example.exists():
        shutil.copy(config_example, dist_dir / "config" / "config.example.yaml")
        print("\n📄 config.example.yaml copiado para dist/config/")
    
    # Copia README
    readme = base_dir / "README.md"
    if readme.exists():
        shutil.copy(readme, dist_dir / "README.md")
        print("📄 README.md copiado para dist/")
    
    # Cria arquivo LEIA-ME.txt
    leia_me = dist_dir / "LEIA-ME.txt"
    leia_me.write_text("""
================================================================================
           SISTEMA DE REGISTRO AUTOMÁTICO DE PONTOS SSG
================================================================================

REQUISITOS DO SISTEMA:
----------------------
- Windows 10 ou superior (64 bits)
- Google Chrome instalado (recomendado, mas não obrigatório)
- Conexão com internet
- NÃO é necessário ter Python instalado

NOTA: Na primeira execução, o sistema baixará automaticamente o navegador
Chromium (~150 MB). Isso acontece apenas uma vez e pode demorar alguns minutos.

================================================================================

CONFIGURAÇÃO INICIAL:
---------------------
1. Renomeie 'config/config.example.yaml' para 'config/config.yaml'

2. Edite 'config/config.yaml' com suas credenciais:
   
   credentials:
     username: "seu.usuario"
     password: "sua_senha"
     totp_secret: "SUASECRETKEYAQUI"   # Opcional - ver seção 2FA abaixo
   
   - username: seu usuário do SSG (ex: joao.silva)
   - password: sua senha do SSG
   - totp_secret: (OPCIONAL) secret key do 2FA para login automático
     Se não configurado, você precisará digitar o código 2FA manualmente.

3. Coloque seu arquivo de pontos em 'data/pontos/pontos.xlsx'
   O arquivo deve conter as colunas: data, entrada, saida_almoco, retorno_almoco, saida

EXECUÇÃO:
---------
1. Execute 'RegistroPontosSSG.exe'
2. O sistema irá:
   - Ler o arquivo de pontos
   - Abrir o navegador e fazer login
   - Preencher o código 2FA automaticamente (se totp_secret configurado)
   - Preencher os pontos automaticamente
   - Aguardar sua confirmação no modal final

PRIMEIRA EXECUÇÃO:
------------------
Na primeira vez, o Playwright precisará baixar o navegador Chrome.
Isso pode demorar alguns minutos dependendo da sua internet.

================================================================================
                        2FA AUTOMÁTICO (OPCIONAL)
================================================================================

Para automatizar o preenchimento do código 2FA, você precisa obter a 
"secret key" do seu autenticador. Siga os passos:

1. SOLICITAR TROCA DE DISPOSITIVO
   - Abra um chamado na Sysmap solicitando TROCA DE DISPOSITIVO DE 2FA
   - Siga as instruções do suporte

2. DURANTE A RECONFIGURAÇÃO
   - Será exibido um QR code na tela
   - TIRE UM PRINT/SCREENSHOT do QR code e salve como imagem
   - IMPORTANTE: Escaneie o QR code no seu Authenticator ANTES de continuar
     (isso é seu backup para login manual!)

3. EXTRAIR A SECRET KEY DO QR CODE
   
   Use um dos métodos abaixo:
   
   A) Sites de Leitura de QR Code (Recomendado):
      - https://webqr.com (processa localmente, mais seguro)
      - https://qrcodescan.in
      - https://4qrcode.com/scan-qr-code.php
      - https://zxing.org/w/decode.jspx
      
      Faça upload da imagem e copie o valor após "secret=" na URL exibida.
      
      Exemplo de URL exibida:
      otpauth://totp/SysMap:usuario?secret=ABC123XYZ&issuer=SysMap
                                          ^^^^^^^^^^
                                          Esta é a secret key!
   
   B) App Aegis Authenticator (Android - Gratuito):
      - Escaneie o QR code
      - Toque e segure na conta adicionada
      - Selecione "Editar" para ver a secret key
   
   C) App 2FAS (Android/iOS - Gratuito):
      - Escaneie o QR code
      - Acesse configurações da conta
      - Opção "Exibir chave secreta"
   
   D) Extensão Authenticator (Chrome/Firefox/Edge):
      - Instale a extensão "Authenticator" do navegador
      - Ao escanear, a secret key é exibida automaticamente

4. CONFIGURAR NO config.yaml
   
   Adicione a linha no arquivo config/config.yaml dentro de credentials:
   
   credentials:
     username: "seu.usuario"
     password: "sua_senha"
     totp_secret: "ABC123XYZ"   # Cole sua secret key aqui

5. LIMPEZA (IMPORTANTE!)
   - APAGUE a imagem do QR code do seu computador
   - Limpe o histórico do site usado para decode (se aplicável)
   - Nunca compartilhe o QR code ou a secret key com ninguém

IMPORTANTE: Mesmo com 2FA automático configurado, SEMPRE mantenha o 
Authenticator no celular como backup para login manual caso necessário.

SOLUÇÃO DE PROBLEMAS:
--------------------
- Se o login falhar, verifique se username/password estão corretos
- Se o 2FA automático não funcionar, verifique a secret key
- Logs são gravados na pasta 'logs/' para diagnóstico
- Em caso de erro, a tela permanecerá aberta mostrando a mensagem

SUPORTE:
--------
Em caso de dúvidas, consulte o README.md

================================================================================
""", encoding="utf-8")
    print("📄 LEIA-ME.txt criado em dist/")


def criar_zip_distribuicao(base_dir: Path):
    """Cria arquivo ZIP com todos os arquivos necessários para distribuição."""
    
    dist_dir = base_dir / "dist"
    zip_path = base_dir / "dist" / "RegistroPontosSSG.zip"
    
    # Remove ZIP anterior se existir
    if zip_path.exists():
        zip_path.unlink()
        print("\n🗑️  ZIP anterior removido")
    
    print("📦 Criando arquivo ZIP para distribuição...")
    
    # Lista de arquivos/pastas a incluir no ZIP
    arquivos_incluir = [
        "RegistroPontosSSG.exe",
        "config/config.example.yaml",
        "LEIA-ME.txt",
        "README.md",
    ]
    
    # Pastas vazias a criar no ZIP
    pastas_vazias = [
        "data/pontos/",
        "logs/",
    ]
    
    with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
        # Adiciona arquivos
        for arquivo in arquivos_incluir:
            arquivo_path = dist_dir / arquivo
            if arquivo_path.exists():
                # Adiciona dentro de uma pasta "RegistroPontosSSG/"
                zipf.write(arquivo_path, f"RegistroPontosSSG/{arquivo}")
        
        # Cria pastas vazias (adiciona arquivo .gitkeep vazio)
        for pasta in pastas_vazias:
            # Adiciona um arquivo vazio para garantir que a pasta exista
            zipf.writestr(f"RegistroPontosSSG/{pasta}.gitkeep", "")
    
    # Tamanho do arquivo
    tamanho_mb = zip_path.stat().st_size / (1024 * 1024)
    
    print(f"✅ ZIP criado: {zip_path}")
    print(f"   Tamanho: {tamanho_mb:.1f} MB")
    print(f"\n📤 Arquivo pronto para distribuição!")
    print(f"   Compartilhe: dist/RegistroPontosSSG.zip")


if __name__ == "__main__":
    build()
