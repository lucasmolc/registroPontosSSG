"""
Configuração do sistema de logs.
"""
import sys
from pathlib import Path
from loguru import logger


def configurar_logger(nivel: str = "INFO", arquivo_log: Path = None, console: bool = False) -> None:
    """
    Configura o logger do sistema.
    
    Args:
        nivel: Nível de log (DEBUG, INFO, WARNING, ERROR, CRITICAL).
        arquivo_log: Caminho para o arquivo de log.
        console: Se True, também exibe logs no console (padrão: False).
    """
    # Remove o handler padrão
    logger.remove()
    
    # Formato do log para arquivo
    formato_arquivo = (
        "{time:YYYY-MM-DD HH:mm:ss} | "
        "{level: <8} | "
        "{name}:{function}:{line} | "
        "{message}"
    )
    
    # Formato do log para console (com cores)
    formato_console = (
        "<green>{time:YYYY-MM-DD HH:mm:ss}</green> | "
        "<level>{level: <8}</level> | "
        "<level>{message}</level>"
    )
    
    # Adiciona handler para console apenas se solicitado
    if console:
        logger.add(
            sys.stderr,
            format=formato_console,
            level=nivel,
            colorize=True
        )
    
    # Adiciona handler para arquivo (sempre, se especificado)
    if arquivo_log:
        # Cria diretório de logs se não existir
        arquivo_log.parent.mkdir(parents=True, exist_ok=True)
        
        logger.add(
            str(arquivo_log),
            format=formato_arquivo,
            level=nivel,
            rotation="10 MB",
            retention="30 days",
            compression="zip",
            encoding="utf-8"
        )
        
        logger.info(f"Logger configurado - Nível: {nivel}, Arquivo: {arquivo_log}")
