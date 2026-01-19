"""
Módulo de configurações do sistema.
"""
import os
from pathlib import Path
import yaml
from dotenv import load_dotenv

# Carrega variáveis de ambiente
load_dotenv()


class Settings:
    """Classe para gerenciar configurações do sistema."""
    
    def __init__(self, config_path: str = None):
        """
        Inicializa as configurações.
        
        Args:
            config_path: Caminho para o arquivo de configuração YAML.
        """
        self.base_dir = Path(__file__).parent.parent
        self.config_path = config_path or self.base_dir / "config" / "config.yaml"
        self._config = self._load_config()
    
    def _load_config(self) -> dict:
        """Carrega o arquivo de configuração YAML."""
        try:
            with open(self.config_path, "r", encoding="utf-8") as file:
                return yaml.safe_load(file)
        except FileNotFoundError:
            raise FileNotFoundError(
                f"Arquivo de configuração não encontrado: {self.config_path}\n"
                f"Copie o arquivo config.example.yaml para config.yaml e preencha suas credenciais."
            )
        except yaml.YAMLError as e:
            raise ValueError(f"Erro ao ler arquivo YAML: {e}")
    
    @property
    def ssg_url(self) -> str:
        """URL base do SSG."""
        return self._config.get("ssg", {}).get("url", "")
    
    @property
    def timesheet_url(self) -> str:
        """URL da página de timesheet do SSG."""
        return self._config.get("ssg", {}).get("timesheet_url", "")
    
    @property
    def username(self) -> str:
        """Nome de usuário (prioriza variável de ambiente)."""
        return os.getenv("SSG_USERNAME") or self._config.get("credentials", {}).get("username", "")
    
    @property
    def password(self) -> str:
        """Senha (prioriza variável de ambiente)."""
        return os.getenv("SSG_PASSWORD") or self._config.get("credentials", {}).get("password", "")
    
    @property
    def arquivo_pontos_diretorio(self) -> Path:
        """Diretório do arquivo de pontos."""
        diretorio = self._config.get("arquivo_pontos", {}).get("diretorio", "data/pontos")
        return self.base_dir / diretorio
    
    @property
    def arquivo_pontos_nome(self) -> str:
        """Nome do arquivo de pontos."""
        return self._config.get("arquivo_pontos", {}).get("nome_arquivo", "pontos.xlsx")
    
    @property
    def arquivo_pontos_caminho(self) -> Path:
        """Caminho completo do arquivo de pontos."""
        return self.arquivo_pontos_diretorio / self.arquivo_pontos_nome
    
    @property
    def arquivo_pontos_formato(self) -> str:
        """Formato do arquivo de pontos."""
        return self._config.get("arquivo_pontos", {}).get("formato", "xlsx")
    
    # Propriedades de validação
    @property
    def bloquear_horarios_redondos(self) -> bool:
        """Se deve bloquear horários redondos."""
        return self._config.get("validacao", {}).get("bloquear_horarios_redondos", True)
    
    @property
    def dias_verificar_duplicados(self) -> int:
        """Quantidade de dias para verificar duplicados."""
        return self._config.get("validacao", {}).get("dias_verificar_duplicados", 5)
    
    @property
    def bloquear_horarios_duplicados(self) -> bool:
        """Se deve bloquear horários duplicados."""
        return self._config.get("validacao", {}).get("bloquear_horarios_duplicados", True)
    
    @property
    def bloquear_almoco_1_hora_exata(self) -> bool:
        """Se deve bloquear almoço de exatamente 1 hora."""
        return self._config.get("validacao", {}).get("bloquear_almoco_1_hora_exata", True)
    
    # Propriedades de automação
    @property
    def timeout(self) -> int:
        """Timeout para operações de automação."""
        return self._config.get("automacao", {}).get("timeout", 30000)
    
    @property
    def headless(self) -> bool:
        """Modo headless do navegador."""
        return self._config.get("automacao", {}).get("headless", False)
    
    @property
    def slow_mo(self) -> int:
        """Delay entre ações do navegador."""
        return self._config.get("automacao", {}).get("slow_mo", 100)
    
    @property
    def selecionar_mes_atual(self) -> bool:
        """Se deve selecionar mês atual ao entrar na página."""
        return self._config.get("automacao", {}).get("selecionar_mes_atual", True)
    
    @property
    def ignorar_datas_existentes(self) -> bool:
        """Se deve ignorar datas já cadastradas."""
        return self._config.get("automacao", {}).get("ignorar_datas_existentes", True)
    
    @property
    def usar_chrome_sistema(self) -> bool:
        """Se deve usar o Chrome instalado no sistema (ajuda a passar no captcha)."""
        return self._config.get("automacao", {}).get("usar_chrome_sistema", True)
    
    @property
    def chrome_path(self) -> str:
        """Caminho do executável do Chrome."""
        return self._config.get("automacao", {}).get("chrome_path", "")
    
    @property
    def usar_perfil_chrome(self) -> bool:
        """Se deve usar o perfil padrão do Chrome do usuário."""
        return self._config.get("automacao", {}).get("usar_perfil_chrome", False)
    
    # Propriedades de log
    @property
    def log_nivel(self) -> str:
        """Nível de log."""
        return self._config.get("log", {}).get("nivel", "INFO")
    
    @property
    def log_arquivo(self) -> Path:
        """Caminho do arquivo de log."""
        caminho = self._config.get("log", {}).get("arquivo", "logs/registro_pontos.log")
        return self.base_dir / caminho
    
    def get_regras_validacao(self) -> dict:
        """Retorna as regras de validação como dicionário."""
        return {
            "bloquear_horarios_redondos": self.bloquear_horarios_redondos,
            "dias_verificar_duplicados": self.dias_verificar_duplicados,
            "bloquear_horarios_duplicados": self.bloquear_horarios_duplicados,
            "bloquear_almoco_1_hora_exata": self.bloquear_almoco_1_hora_exata,
        }
