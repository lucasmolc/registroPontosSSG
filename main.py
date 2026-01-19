"""
Sistema de Registro Automático de Pontos SSG

Este sistema lê um arquivo de pontos (Excel/CSV) e registra
automaticamente os pontos no sistema SSG.
"""

# Bootstrap - Configura ambiente automaticamente
from bootstrap import ensure_environment
if not ensure_environment():
    print("\n❌ Falha ao configurar ambiente. Verifique os erros acima.")
    exit(1)

# Imports após garantir ambiente
import sys
from pathlib import Path

from loguru import logger

from config import Settings
from src.logger_config import configurar_logger
from src.leitor_pontos import LeitorPontos
from src.automacao_ssg import AutomacaoSSG


def exibir_regras_validacao(settings: Settings) -> None:
    """Exibe as regras de validação configuradas."""
    print("\n📋 Regras de Ajuste Automático:")
    print("-" * 50)
    print(f"  • Ajustar horários redondos (:00): {'Sim (+1min)' if settings.bloquear_horarios_redondos else 'Não'}")
    print(f"  • Verificar duplicados nos últimos: {settings.dias_verificar_duplicados} dias")
    print(f"  • Ajustar horários duplicados: {'Sim (+1min)' if settings.bloquear_horarios_duplicados else 'Não'}")
    print(f"  • Ajustar almoço de 1h exata: {'Sim (+1min)' if settings.bloquear_almoco_1_hora_exata else 'Não'}")
    print(f"  • Selecionar mês atual: {'Sim' if settings.selecionar_mes_atual else 'Não'}")
    print(f"  • Ignorar datas já cadastradas: {'Sim' if settings.ignorar_datas_existentes else 'Não'}")
    print("-" * 50)


def main():
    """Função principal do sistema."""
    print("=" * 60)
    print("  Sistema de Registro Automático de Pontos SSG")
    print("  SSG: https://ssg.sysmap.com.br/")
    print("  Portal: https://portal.sysmap.com.br/")
    print("=" * 60)
    print()
    
    try:
        # Carrega configurações
        settings = Settings()
        
        # Configura logger (apenas arquivo, console limpo)
        configurar_logger(
            nivel=settings.log_nivel,
            arquivo_log=settings.log_arquivo,
            console=False  # Logs apenas no arquivo
        )
        
        logger.info("Iniciando sistema de registro de pontos...")
        
        # Valida credenciais
        if not settings.username or not settings.password:
            logger.error("Credenciais não configuradas.")
            print("\n❌ ERRO: Credenciais não configuradas!")
            print("   Configure no arquivo config/config.yaml ou variáveis de ambiente.")
            print("   Use config/config.example.yaml como base.")
            sys.exit(1)
        
        # Verifica se o diretório de pontos existe
        if not settings.arquivo_pontos_diretorio.exists():
            settings.arquivo_pontos_diretorio.mkdir(parents=True, exist_ok=True)
            logger.info(f"Diretório de pontos criado: {settings.arquivo_pontos_diretorio}")
        
        # Verifica se o arquivo de pontos existe
        if not settings.arquivo_pontos_caminho.exists():
            logger.error(f"Arquivo de pontos não encontrado: {settings.arquivo_pontos_caminho}")
            print(f"\n❌ ERRO: Arquivo de pontos não encontrado!")
            print(f"   Esperado em: {settings.arquivo_pontos_caminho}")
            print(f"\n   Crie um arquivo Excel com as colunas:")
            print("   data | entrada | saida_almoco | retorno_almoco | saida | observacao")
            sys.exit(1)
        
        # Exibe regras de validação
        exibir_regras_validacao(settings)
        
        # Lê arquivo de pontos
        leitor = LeitorPontos(
            caminho_arquivo=settings.arquivo_pontos_caminho,
            formato=settings.arquivo_pontos_formato
        )
        registros = leitor.ler_pontos()
        
        if not registros:
            logger.warning("Nenhum registro de ponto encontrado no arquivo.")
            print("\n⚠️  Nenhum registro de ponto encontrado no arquivo.")
            sys.exit(0)
        
        logger.info(f"Total de {len(registros)} registro(s) a processar")
        
        # Exibe resumo dos registros
        print("\n📅 Registros a serem processados:")
        print("-" * 60)
        for i, registro in enumerate(registros, 1):
            print(f"  {i:2}. {registro}")
        print("-" * 60)
        print(f"  Total: {len(registros)} registro(s)")
        print()
        
        # Confirmação do usuário
        confirmacao = input("▶️  Deseja continuar com o registro? (s/n): ").strip().lower()
        if confirmacao != "s":
            logger.info("Operação cancelada pelo usuário.")
            print("\n⏹️  Operação cancelada.")
            sys.exit(0)
        
        # Inicia automação
        print("\n🚀 Iniciando automação...\n")
        print("⚠️  Você precisará inserir o código de validação (2FA) no navegador.\n")
        
        with AutomacaoSSG(settings) as automacao:
            # Realiza login
            print("🔐 Realizando login...")
            if not automacao.fazer_login():
                logger.error("Falha no login. Verifique as credenciais.")
                print("\n❌ Falha no login. Verifique as credenciais.")
                sys.exit(1)
            print("✅ Login realizado com sucesso!\n")
            
            # Seleciona mês atual e filtra
            print("📆 Selecionando mês atual e filtrando...")
            if not automacao.selecionar_mes_atual_e_filtrar():
                logger.error("Falha ao selecionar mês atual.")
                print("\n❌ Falha ao selecionar mês atual.")
                sys.exit(1)
            print("✅ Mês atual selecionado!\n")
            
            # Obtém datas já cadastradas
            print("🔍 Verificando datas já cadastradas...")
            automacao.obter_datas_cadastradas()
            automacao.obter_horarios_existentes()
            
            if automacao.datas_cadastradas:
                print(f"   Encontradas {len(automacao.datas_cadastradas)} data(s) já cadastrada(s)")
            print()
            
            # Processa cada registro
            sucessos = 0
            falhas = 0
            ignorados = 0
            total_ajustes = []
            
            for i, registro in enumerate(registros, 1):
                print(f"📝 [{i}/{len(registros)}] Processando {registro.data}...", end=" ")
                
                # Verifica se já está cadastrado
                if settings.ignorar_datas_existentes and automacao.data_ja_cadastrada(registro.data):
                    print("⏭️  Já cadastrado - ignorando")
                    ignorados += 1
                    continue
                
                sucesso, ajustes = automacao.registrar_ponto(registro)
                if sucesso:
                    if ajustes:
                        print(f"✅ (com {len(ajustes)} ajuste(s))")
                        total_ajustes.extend([(registro.data, a) for a in ajustes])
                    else:
                        print("✅")
                    sucessos += 1
                else:
                    print("❌")
                    falhas += 1
        
        # Exibe ajustes realizados
        if total_ajustes:
            print("\n" + "-" * 60)
            print("  🔧 AJUSTES AUTOMÁTICOS REALIZADOS:")
            print("-" * 60)
            for data, ajuste in total_ajustes:
                print(f"  [{data}] {ajuste}")
            print("-" * 60)
        
        # Exibe resumo final
        print("\n" + "=" * 60)
        print("  📊 RESUMO DO PROCESSAMENTO")
        print("=" * 60)
        print(f"  Total de registros: {len(registros)}")
        print(f"  ✅ Sucessos: {sucessos}")
        print(f"  ⏭️  Ignorados (já cadastrados): {ignorados}")
        print(f"  ❌ Falhas: {falhas}")
        if total_ajustes:
            print(f"  🔧 Ajustes automáticos: {len(total_ajustes)}")
        print("=" * 60)
        
        logger.info(f"Processamento concluído - Sucessos: {sucessos}, Ignorados: {ignorados}, Falhas: {falhas}, Ajustes: {len(total_ajustes)}")
        
    except FileNotFoundError as e:
        logger.error(f"Arquivo não encontrado: {e}")
        print(f"\n❌ ERRO: {e}")
        sys.exit(1)
    except Exception as e:
        logger.exception(f"Erro inesperado: {e}")
        print(f"\n❌ Erro inesperado: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
