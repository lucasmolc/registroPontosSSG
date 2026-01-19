"""
Módulo de automação para o sistema SSG.
"""
from typing import Optional, List, Set, Tuple
from datetime import datetime
from playwright.sync_api import sync_playwright, Page, Browser, BrowserContext
from playwright_stealth import Stealth
from loguru import logger
import shutil
import os

from config import Settings
from src.leitor_pontos import RegistroPonto
from src.validador_horarios import ValidadorHorarios, RegrasValidacao


class AutomacaoSSG:
    """Classe responsável pela automação do sistema SSG."""
    
    def __init__(self, settings: Settings):
        """
        Inicializa a automação SSG.
        
        Args:
            settings: Objeto de configurações.
        """
        self.settings = settings
        self.playwright = None
        self.browser: Optional[Browser] = None
        self.context: Optional[BrowserContext] = None
        self.page: Optional[Page] = None
        self.datas_cadastradas: Set[str] = set()
        
        # Inicializa validador de horários
        regras = RegrasValidacao(
            bloquear_horarios_redondos=settings.bloquear_horarios_redondos,
            dias_verificar_duplicados=settings.dias_verificar_duplicados,
            bloquear_horarios_duplicados=settings.bloquear_horarios_duplicados,
            bloquear_almoco_1_hora_exata=settings.bloquear_almoco_1_hora_exata
        )
        self.validador = ValidadorHorarios(regras)
    
    def iniciar(self) -> None:
        """Inicia o navegador com playwright-stealth para evitar detecção."""
        logger.info("Iniciando navegador...")
        
        self.playwright = sync_playwright().start()
        
        # Usa diretório de dados persistente para manter cookies/sessão
        user_data_dir = self.settings.base_dir / "browser_data"
        user_data_dir.mkdir(parents=True, exist_ok=True)
        
        # Contexto persistente com configurações stealth
        self.context = self.playwright.chromium.launch_persistent_context(
            user_data_dir=str(user_data_dir),
            headless=self.settings.headless,
            slow_mo=self.settings.slow_mo,
            viewport={"width": 1366, "height": 768},
            locale="pt-BR",
            timezone_id="America/Sao_Paulo",
            args=[
                "--disable-blink-features=AutomationControlled",
                "--disable-infobars",
                "--disable-extensions",
                "--disable-gpu",
                "--disable-dev-shm-usage",
                "--log-level=3",  # Suprime logs do Chrome
            ],
            ignore_default_args=["--enable-automation"],
        )
        
        self.page = self.context.pages[0] if self.context.pages else self.context.new_page()
        self.page.set_default_timeout(self.settings.timeout)
        
        # Aplica stealth para evitar detecção
        stealth = Stealth()
        stealth.apply_stealth_sync(self.page)
        
        logger.info("Navegador iniciado com sucesso")
    
    def encerrar(self) -> None:
        """Encerra o navegador e a sessão."""
        logger.info("Encerrando navegador...")
        
        if self.context:
            self.context.close()
        if self.playwright:
            self.playwright.stop()
        
        logger.info("Navegador encerrado")
    
    def fazer_login(self) -> bool:
        """
        Realiza o login no sistema SSG.
        
        Fluxo:
        1. Acessa https://ssg.sysmap.com.br/
        2. Aguarda verificação de segurança (Cloudflare)
        3. Aguarda redirecionamento para https://portal.sysmap.com.br/wp-login.php
        4. Preenche login e senha
        5. Aguarda usuário completar validação (2FA)
        6. Aguarda redirecionamento para https://portal.sysmap.com.br/
        7. Navega para o timesheet
        
        Returns:
            True se o login foi bem sucedido, False caso contrário.
        """
        logger.info("Realizando login no SSG...")
        
        try:
            # 1. Acessa a página principal do SSG
            url_ssg = "https://ssg.sysmap.com.br/"
            logger.info(f"Acessando {url_ssg}")
            print(f"   Acessando {url_ssg}")
            self.page.goto(url_ssg)
            
            # 2. Aguarda verificação de segurança (Cloudflare)
            print("\n" + "=" * 50)
            print("  ⏳ VERIFICAÇÃO DE SEGURANÇA")
            print("=" * 50)
            print("  Aguardando verificação automática...")
            print("  (Pode levar alguns segundos)")
            print("=" * 50)
            
            logger.info("Aguardando verificação de segurança (Cloudflare)...")
            
            # Aguarda até que a URL mude para o portal de login
            url_login = "https://portal.sysmap.com.br/wp-login.php"
            self.page.wait_for_url(
                lambda url: "portal.sysmap.com.br" in url or "wp-login" in url,
                timeout=120000  # 2 minutos para verificação
            )
            
            print("  ✅ Verificação concluída!")
            print("=" * 50 + "\n")
            
            # 3. Aguarda a página de login carregar
            self.page.wait_for_load_state("networkidle")
            logger.info(f"Página de login carregada: {self.page.url}")
            
            # 4. Preenche credenciais
            print("   Preenchendo credenciais...")
            logger.info("Preenchendo credenciais...")
            
            # Aguarda o campo de usuário estar visível
            self.page.wait_for_selector(
                'input[name="log"], input[id="user_login"], input[name="username"]',
                state="visible",
                timeout=30000
            )
            
            # Preenche usuário (WordPress usa 'log' como nome do campo)
            campo_usuario = self.page.locator(
                'input[name="log"], input[id="user_login"], input[name="username"]'
            ).first
            campo_usuario.fill(self.settings.username)
            
            # Preenche senha (WordPress usa 'pwd' como nome do campo)
            campo_senha = self.page.locator(
                'input[name="pwd"], input[id="user_pass"], input[type="password"]'
            ).first
            campo_senha.fill(self.settings.password)
            
            logger.info("Credenciais preenchidas")
            print("   ✅ Credenciais preenchidas!")
            
            # 5. Clica no botão de login
            botao_login = self.page.locator(
                'input[type="submit"], button[type="submit"], input[name="wp-submit"]'
            ).first
            botao_login.click()
            
            # 6. Aguarda usuário completar validação (2FA)
            print("\n" + "=" * 50)
            print("  🔐 AGUARDANDO VALIDAÇÃO")
            print("=" * 50)
            print("  Complete o login no navegador.")
            print("  (Insira o código 2FA se solicitado)")
            print("=" * 50)
            
            logger.info("Aguardando usuário completar validação...")
            
            # Aguarda até que o login seja finalizado (URL muda para portal principal)
            self.page.wait_for_url(
                lambda url: url == "https://portal.sysmap.com.br/" or 
                           (url.startswith("https://portal.sysmap.com.br") and "wp-login" not in url and "wp-admin" not in url),
                timeout=300000  # 5 minutos para completar login
            )
            
            print("  ✅ Login concluído!")
            print("=" * 50 + "\n")
            
            # 7. Navega para a página de timesheet
            logger.info(f"Navegando para timesheet: {self.settings.timesheet_url}")
            print(f"   Navegando para timesheet...")
            self.page.goto(self.settings.timesheet_url)
            self.page.wait_for_load_state("networkidle")
            
            logger.info("Login realizado com sucesso")
            return True
            
        except Exception as e:
            logger.error(f"Erro ao realizar login: {e}")
            return False
    
    def selecionar_mes_atual_e_filtrar(self) -> bool:
        """
        Seleciona o mês atual e clica em filtrar.
        
        Returns:
            True se a operação foi bem sucedida, False caso contrário.
        """
        if not self.settings.selecionar_mes_atual:
            return True
            
        logger.info("Selecionando mês atual e filtrando...")
        
        try:
            # TODO: Ajustar seletores conforme a página real do SSG
            # Exemplo genérico para seleção de mês:
            
            # Opção 1: Select dropdown
            # mes_atual = datetime.now().month
            # self.page.select_option('select[name="mes"]', str(mes_atual))
            
            # Opção 2: Radio button ou checkbox para "Mês Atual"
            # self.page.click('input[value="mes_atual"]')
            # self.page.click('label:has-text("Mês Atual")')
            
            # Clica no botão de filtrar
            # self.page.click('button:has-text("Filtrar"), input[value="Filtrar"]')
            
            self.page.wait_for_load_state("networkidle")
            
            logger.info("Mês atual selecionado e filtrado")
            return True
            
        except Exception as e:
            logger.error(f"Erro ao selecionar mês atual: {e}")
            return False
    
    def obter_datas_cadastradas(self) -> Set[str]:
        """
        Obtém as datas já cadastradas no SSG.
        
        Returns:
            Conjunto de datas já cadastradas (formato DD/MM/YYYY).
        """
        logger.info("Obtendo datas já cadastradas no SSG...")
        
        try:
            datas = set()
            
            # TODO: Ajustar seletores conforme a página real do SSG
            # Exemplo: buscar todas as linhas da tabela de timesheet
            
            # Opção 1: Tabela com datas
            # linhas = self.page.locator('table.timesheet tbody tr').all()
            # for linha in linhas:
            #     data_cell = linha.locator('td:first-child').text_content()
            #     if data_cell:
            #         datas.add(data_cell.strip())
            
            # Opção 2: Divs ou outros elementos
            # elementos_data = self.page.locator('.registro-data').all()
            # for elem in elementos_data:
            #     datas.add(elem.text_content().strip())
            
            self.datas_cadastradas = datas
            logger.info(f"Encontradas {len(datas)} datas já cadastradas")
            
            return datas
            
        except Exception as e:
            logger.error(f"Erro ao obter datas cadastradas: {e}")
            return set()
    
    def obter_horarios_existentes(self) -> dict:
        """
        Obtém os horários já cadastrados para verificação de duplicados.
        
        Returns:
            Dicionário {data: [horarios]} com os horários existentes.
        """
        logger.info("Obtendo horários existentes para verificação de duplicados...")
        
        horarios = {}
        
        try:
            # TODO: Ajustar seletores conforme a página real do SSG
            # Buscar horários das datas já cadastradas
            
            # Exemplo:
            # linhas = self.page.locator('table.timesheet tbody tr').all()
            # for linha in linhas:
            #     data = linha.locator('td.data').text_content().strip()
            #     entrada = linha.locator('td.entrada').text_content().strip()
            #     saida_almoco = linha.locator('td.saida-almoco').text_content().strip()
            #     retorno_almoco = linha.locator('td.retorno-almoco').text_content().strip()
            #     saida = linha.locator('td.saida').text_content().strip()
            #     
            #     if data:
            #         horarios[data] = [entrada, saida_almoco, retorno_almoco, saida]
            
            # Carrega horários no validador
            self.validador.carregar_horarios_existentes(horarios)
            
            return horarios
            
        except Exception as e:
            logger.error(f"Erro ao obter horários existentes: {e}")
            return {}
    
    def data_ja_cadastrada(self, data: str) -> bool:
        """
        Verifica se uma data já está cadastrada.
        
        Args:
            data: Data a verificar (DD/MM/YYYY).
            
        Returns:
            True se a data já está cadastrada, False caso contrário.
        """
        return data in self.datas_cadastradas
    
    def clicar_adicionar_registro(self) -> bool:
        """
        Clica no botão de adicionar novo registro.
        
        Returns:
            True se clicou com sucesso, False caso contrário.
        """
        logger.info("Clicando no botão de adicionar registro...")
        
        try:
            # TODO: Ajustar seletor conforme a página real do SSG
            # Exemplos de possíveis seletores:
            # self.page.click('button:has-text("Adicionar")')
            # self.page.click('a.btn-adicionar')
            # self.page.click('input[value="Novo Registro"]')
            # self.page.click('#btnAdicionar')
            
            self.page.wait_for_load_state("networkidle")
            
            logger.info("Botão de adicionar registro clicado")
            return True
            
        except Exception as e:
            logger.error(f"Erro ao clicar em adicionar registro: {e}")
            return False
    
    def registrar_ponto(self, registro: RegistroPonto) -> Tuple[bool, List[str]]:
        """
        Registra um ponto no sistema SSG com ajustes automáticos.
        
        Args:
            registro: Objeto RegistroPonto com os dados a serem registrados.
            
        Returns:
            Tupla (sucesso, lista de ajustes realizados).
        """
        logger.info(f"Processando registro: {registro}")
        
        # Verifica se a data já está cadastrada
        if self.settings.ignorar_datas_existentes and self.data_ja_cadastrada(registro.data):
            logger.warning(f"Data {registro.data} já cadastrada - ignorando")
            return True, []  # Retorna True pois não é um erro
        
        # Ajusta o registro automaticamente conforme regras
        registro_ajustado = self.validador.ajustar_registro(
            registro.data,
            registro.entrada,
            registro.saida_almoco,
            registro.retorno_almoco,
            registro.saida
        )
        
        # Log dos ajustes realizados
        if registro_ajustado.teve_ajustes():
            logger.info(f"Ajustes realizados para {registro.data}:")
            for ajuste in registro_ajustado.ajustes_realizados:
                logger.info(f"  ↳ {ajuste}")
        
        try:
            # Clica no botão de adicionar registro
            if not self.clicar_adicionar_registro():
                return False, []
            
            # TODO: Implementar a lógica de preenchimento conforme a página real do SSG
            # Usa os valores ajustados:
            
            # Preenche data
            # self.page.fill('input[name="data"]', registro_ajustado.data)
            
            # Preenche horário de entrada
            # self.page.fill('input[name="entrada"]', registro_ajustado.entrada)
            
            # Preenche saída para almoço
            # self.page.fill('input[name="saida_almoco"]', registro_ajustado.saida_almoco)
            
            # Preenche retorno do almoço
            # self.page.fill('input[name="retorno_almoco"]', registro_ajustado.retorno_almoco)
            
            # Preenche horário de saída
            # self.page.fill('input[name="saida"]', registro_ajustado.saida)
            
            # Preenche observação se houver
            # if registro.observacao:
            #     self.page.fill('textarea[name="observacao"]', registro.observacao)
            
            # Clica no botão de salvar
            # self.page.click('button[type="submit"], input[value="Salvar"]')
            
            # Aguarda confirmação
            self.page.wait_for_load_state("networkidle")
            
            # Registra horários utilizados no validador (usa os ajustados)
            self.validador.registrar_horarios_utilizados(
                registro_ajustado.data,
                [registro_ajustado.entrada, registro_ajustado.saida_almoco, 
                 registro_ajustado.retorno_almoco, registro_ajustado.saida]
            )
            
            # Adiciona data às cadastradas
            self.datas_cadastradas.add(registro.data)
            
            logger.info(f"Ponto registrado com sucesso: {registro.data}")
            return True, registro_ajustado.ajustes_realizados
            
        except Exception as e:
            logger.error(f"Erro ao registrar ponto {registro.data}: {e}")
            return False, []
    
    def navegar_para_timesheet(self) -> bool:
        """
        Navega para a página de timesheet.
        
        Returns:
            True se navegou com sucesso, False caso contrário.
        """
        logger.info("Navegando para página de timesheet...")
        
        try:
            self.page.goto(self.settings.timesheet_url)
            self.page.wait_for_load_state("networkidle")
            
            logger.info("Navegação para timesheet concluída")
            return True
            
        except Exception as e:
            logger.error(f"Erro ao navegar para timesheet: {e}")
            return False
    
    def limpar_dados_navegador(self) -> None:
        """
        Limpa os dados do navegador (cookies, cache, etc.).
        """
        logger.info("Limpando dados do navegador...")
        
        try:
            # Fecha o contexto atual
            if self.context:
                self.context.close()
            
            # Remove o diretório de dados do navegador
            user_data_dir = self.settings.base_dir / "browser_data"
            if os.path.exists(user_data_dir):
                shutil.rmtree(user_data_dir, ignore_errors=True)
                logger.info("Dados do navegador limpos")
            else:
                logger.warning("Diretório de dados do navegador não encontrado")
            
            # Reinicia o playwright
            self.playwright.stop()
            self.playwright = sync_playwright().start()
            
            # Recria o contexto do navegador
            self.context = self.playwright.chromium.launch_persistent_context(
                user_data_dir=str(user_data_dir),
                headless=self.settings.headless,
                slow_mo=self.settings.slow_mo,
                viewport={"width": 1366, "height": 768},
                locale="pt-BR",
                timezone_id="America/Sao_Paulo",
                args=[
                    "--disable-blink-features=AutomationControlled",
                    "--disable-infobars",
                    "--disable-extensions",
                    "--disable-gpu",
                    "--disable-dev-shm-usage",
                    "--log-level=3",  # Suprime logs do Chrome
                ],
                ignore_default_args=["--enable-automation"],
            )
            
            logger.info("Dados do navegador limpos e navegador reiniciado")
        
        except Exception as e:
            logger.error(f"Erro ao limpar dados do navegador: {e}")
    
    def __enter__(self):
        """Context manager - entrada."""
        self.iniciar()
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        """Context manager - saída."""
        self.encerrar()
