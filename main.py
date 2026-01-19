"""
Sistema de Registro Automático de Pontos SSG
"""

# Bootstrap - Configura ambiente automaticamente
from bootstrap import ensure_environment
if not ensure_environment():
    print("\n❌ Falha ao configurar ambiente.")
    exit(1)

# Imports após garantir ambiente
import sys
from pathlib import Path

from loguru import logger

from config import Settings
from src.logger_config import configurar_logger
from src.leitor_pontos import LeitorPontos
from src.automacao_ssg import AutomacaoSSG


def main():
    """Função principal do sistema."""
    print("\n🚀 Registro Automático de Pontos SSG\n")
    
    try:
        # Carrega configurações
        settings = Settings()
        
        # Configura logger (apenas arquivo)
        configurar_logger(
            nivel=settings.log_nivel,
            arquivo_log=settings.log_arquivo,
            console=False
        )
        
        logger.info("Iniciando sistema de registro de pontos...")
        
        # Valida credenciais
        if not settings.username or not settings.password:
            print("❌ Credenciais não configuradas em config/config.yaml")
            sys.exit(1)
        
        # Verifica se o diretório de pontos existe
        if not settings.arquivo_pontos_diretorio.exists():
            settings.arquivo_pontos_diretorio.mkdir(parents=True, exist_ok=True)
        
        # Verifica se o arquivo de pontos existe
        if not settings.arquivo_pontos_caminho.exists():
            print(f"❌ Arquivo não encontrado: {settings.arquivo_pontos_caminho}")
            sys.exit(1)
        
        # Lê arquivo de pontos
        leitor = LeitorPontos(
            caminho_arquivo=settings.arquivo_pontos_caminho,
            formato=settings.arquivo_pontos_formato
        )
        registros = leitor.ler_pontos()
        
        if not registros:
            print("⚠️  Nenhum registro encontrado no arquivo.")
            sys.exit(0)
        
        # Exibe registros
        print(f"📅 {len(registros)} registro(s) a processar:\n")
        for i, registro in enumerate(registros, 1):
            print(f"   {i}. {registro}")
        print()
        
        # Inicia automação
        with AutomacaoSSG(settings) as automacao:
            # Realiza login
            print("🔐 Realizando login...")
            if not automacao.fazer_login():
                print("❌ Falha no login.")
                sys.exit(1)
            print("✅ Login OK\n")
            
            # Seleciona mês atual
            print("📆 Filtrando por mês atual...")
            automacao.selecionar_mes_atual_e_filtrar()
            
            # Obtém datas já cadastradas
            print("🔍 Verificando datas já cadastradas...")
            datas_existentes = automacao.obter_datas_cadastradas()
            automacao.obter_horarios_existentes()
            
            if datas_existentes:
                print(f"   ⚠️  {len(datas_existentes)} data(s) já cadastrada(s): {', '.join(sorted(datas_existentes))}")
            else:
                print("   ✅ Nenhuma data cadastrada no mês atual")
            
            # Processa cada registro
            sucessos = 0
            falhas = 0
            ignorados = 0
            
            for i, registro in enumerate(registros, 1):
                print(f"📝 [{i}/{len(registros)}] {registro.data}...", end=" ")
                
                # Verifica se já está cadastrado
                if settings.ignorar_datas_existentes and automacao.data_ja_cadastrada(registro.data):
                    print("⏭️ já cadastrado")
                    ignorados += 1
                    continue
                
                sucesso, ajustes = automacao.registrar_ponto(registro)
                if sucesso:
                    print("✅")
                    sucessos += 1
                else:
                    print("❌")
                    falhas += 1
            
            # Confirma todos os apontamentos se houve sucesso
            if sucessos > 0:
                print("\n💾 Confirmando apontamentos...")
                if automacao.confirmar_apontamentos():
                    print("✅ Apontamentos salvos!")
                else:
                    print("❌ Erro ao salvar apontamentos")
            
            # Resumo final
            print(f"\n📊 Resultado: ✅ {sucessos} | ⏭️ {ignorados} | ❌ {falhas}")
            
            # BREAKPOINT: Mantém browser aberto para validação
            input("\n⏸️  Pressione ENTER para fechar o navegador...")
        
        logger.info(f"Concluído - Sucessos: {sucessos}, Ignorados: {ignorados}, Falhas: {falhas}")
        
    except FileNotFoundError as e:
        print(f"❌ {e}")
        sys.exit(1)
    except Exception as e:
        logger.exception(f"Erro: {e}")
        print(f"❌ Erro: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
