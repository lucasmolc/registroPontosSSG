"""
Módulo de bootstrap para configuração automática do ambiente.
Verifica e cria o venv, instala dependências automaticamente.
Quando rodando como executável PyInstaller, pula a criação de venv.
"""
import subprocess
import sys
import os
from pathlib import Path


def is_running_as_exe() -> bool:
    """Verifica se está rodando como executável PyInstaller."""
    # PyInstaller define esse atributo quando está rodando como .exe
    return getattr(sys, 'frozen', False) and hasattr(sys, '_MEIPASS')


def get_project_root() -> Path:
    """Retorna o diretório raiz do projeto."""
    if is_running_as_exe():
        # Quando rodando como .exe, usa o diretório do executável
        return Path(sys.executable).parent
    return Path(__file__).parent


def get_venv_path() -> Path:
    """Retorna o caminho do ambiente virtual."""
    return get_project_root() / "venv"


def get_venv_python() -> Path:
    """Retorna o caminho do Python no venv."""
    venv_path = get_venv_path()
    if sys.platform == "win32":
        return venv_path / "Scripts" / "python.exe"
    return venv_path / "bin" / "python"


def is_running_in_venv() -> bool:
    """Verifica se está rodando dentro do venv do projeto."""
    venv_path = get_venv_path()
    if not venv_path.exists():
        return False
    
    # Verifica se o executável atual está dentro do venv
    current_python = Path(sys.executable).resolve()
    venv_python = get_venv_python().resolve()
    
    return current_python == venv_python


def create_venv() -> bool:
    """Cria o ambiente virtual se não existir."""
    venv_path = get_venv_path()
    
    if venv_path.exists():
        return True
    
    print("📦 Criando ambiente virtual...")
    try:
        subprocess.run(
            [sys.executable, "-m", "venv", str(venv_path)],
            check=True,
            capture_output=True
        )
        print("✅ Ambiente virtual criado com sucesso!")
        return True
    except subprocess.CalledProcessError as e:
        print(f"❌ Erro ao criar ambiente virtual: {e}")
        return False


def install_requirements() -> bool:
    """Instala as dependências do requirements.txt."""
    requirements_path = get_project_root() / "requirements.txt"
    venv_python = get_venv_python()
    
    if not requirements_path.exists():
        print("⚠️  Arquivo requirements.txt não encontrado")
        return True
    
    print("📥 Instalando dependências...")
    try:
        subprocess.run(
            [str(venv_python), "-m", "pip", "install", "-r", str(requirements_path), "-q"],
            check=True,
            capture_output=True
        )
        print("✅ Dependências instaladas com sucesso!")
        return True
    except subprocess.CalledProcessError as e:
        print(f"❌ Erro ao instalar dependências: {e}")
        return False


def install_playwright_browsers() -> bool:
    """Instala os navegadores do Playwright."""
    venv_python = get_venv_python()
    
    print("🌐 Verificando navegadores Playwright...")
    try:
        # Verifica se playwright está instalado
        result = subprocess.run(
            [str(venv_python), "-c", "import playwright"],
            capture_output=True
        )
        
        if result.returncode != 0:
            return True  # Playwright não instalado, pula
        
        # Instala chromium silenciosamente
        subprocess.run(
            [str(venv_python), "-m", "playwright", "install", "chromium"],
            capture_output=True
        )
        print("✅ Navegadores configurados!")
        return True
    except Exception:
        # Se falhar, não é crítico
        return True


def check_dependencies_installed() -> bool:
    """Verifica se as dependências principais estão instaladas."""
    try:
        import loguru
        import playwright
        import pandas
        import yaml
        return True
    except ImportError:
        return False


def ensure_environment() -> bool:
    """
    Garante que o ambiente está configurado corretamente.
    Cria venv e instala dependências se necessário.
    
    Quando rodando como .exe (PyInstaller), pula a criação de venv
    pois as dependências já estão empacotadas no executável.
    
    Returns:
        True se o ambiente está pronto, False caso contrário.
    """
    # Se está rodando como executável PyInstaller, não precisa de venv
    # Todas as dependências já estão empacotadas no .exe
    if is_running_as_exe():
        return True
    
    # Se já está no venv com dependências, tudo ok
    if is_running_in_venv() and check_dependencies_installed():
        return True
    
    # Se não está no venv, precisa configurar e reiniciar
    if not is_running_in_venv():
        print("\n🔧 Configurando ambiente...\n")
        
        # Cria venv se não existir
        if not create_venv():
            return False
        
        # Instala dependências
        if not install_requirements():
            return False
        
        # Instala browsers do Playwright
        install_playwright_browsers()
        
        # Reinicia o script no venv usando subprocess para manter stdin funcional
        print("\n🔄 Reiniciando no ambiente virtual...\n")
        venv_python = get_venv_python()
        
        result = subprocess.run(
            [str(venv_python)] + sys.argv,
            cwd=get_project_root()
        )
        sys.exit(result.returncode)
    
    # Está no venv mas falta dependências
    if not check_dependencies_installed():
        print("\n📥 Dependências não encontradas, instalando...\n")
        if not install_requirements():
            return False
        
        # Reinicia para carregar as novas dependências
        print("\n🔄 Reiniciando...\n")
        result = subprocess.run(
            [sys.executable] + sys.argv,
            cwd=get_project_root()
        )
        sys.exit(result.returncode)
    
    return True


if __name__ == "__main__":
    # Teste do bootstrap
    print(f"Python: {sys.executable}")
    print(f"Venv existe: {get_venv_path().exists()}")
    print(f"Rodando no venv: {is_running_in_venv()}")
    print(f"Dependências OK: {check_dependencies_installed()}")
