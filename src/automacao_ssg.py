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
import subprocess
import time
import socket

from config import Settings
from src.leitor_pontos import RegistroPonto
from src.validador_horarios import ValidadorHorarios, RegrasValidacao


def encontrar_porta_livre() -> int:
    """Encontra uma porta TCP livre para o debugging."""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(('', 0))
        return s.getsockname()[1]


def encontrar_chrome() -> str:
    """Encontra o caminho do Chrome instalado no sistema."""
    caminhos_possiveis = [
        os.path.expandvars(r"%ProgramFiles%\Google\Chrome\Application\chrome.exe"),
        os.path.expandvars(r"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"),
        os.path.expandvars(r"%LocalAppData%\Google\Chrome\Application\chrome.exe"),
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    ]
    
    for caminho in caminhos_possiveis:
        if os.path.exists(caminho):
            return caminho
    
    return ""


def formatar_horario(horario: str) -> str:
    """
    Formata um horário garantindo formato HH:MM com 2 dígitos.
    
    Args:
        horario: Horário no formato H:MM ou HH:MM
        
    Returns:
        Horário formatado como HH:MM (ex: 08:00)
    """
    if not horario:
        return horario
    
    try:
        partes = horario.split(":")
        if len(partes) == 2:
            hora = int(partes[0])
            minuto = int(partes[1])
            return f"{hora:02d}:{minuto:02d}"
    except (ValueError, IndexError):
        pass
    
    return horario


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
        
        # Verifica se deve usar o Chrome do sistema
        if self.settings.usar_chrome_sistema:
            self._iniciar_chrome_sistema()
        else:
            self._iniciar_chromium_embutido()
    
    def _iniciar_chrome_sistema(self) -> None:
        """Inicia o Chrome instalado no sistema via CDP (Chrome DevTools Protocol)."""
        logger.info("Usando Chrome instalado no sistema...")
        
        # Encontra o Chrome
        chrome_path = self.settings.chrome_path or encontrar_chrome()
        if not chrome_path or not os.path.exists(chrome_path):
            logger.warning("Chrome não encontrado, usando Chromium embutido")
            self._iniciar_chromium_embutido()
            return
        
        logger.info(f"Chrome encontrado: {chrome_path}")
        
        # Diretório de dados do navegador
        if self.settings.usar_perfil_chrome:
            # Usa o perfil padrão do usuário (com cookies e sessões existentes)
            user_data_dir = os.path.expandvars(r"%LocalAppData%\Google\Chrome\User Data")
        else:
            # Usa diretório separado para o sistema
            user_data_dir = str(self.settings.base_dir / "browser_data")
            os.makedirs(user_data_dir, exist_ok=True)
        
        # Encontra porta livre para debugging
        porta_debug = encontrar_porta_livre()
        
        # Argumentos do Chrome
        args = [
            chrome_path,
            f"--remote-debugging-port={porta_debug}",
            f"--user-data-dir={user_data_dir}",
            "--disable-background-networking",
            "--disable-client-side-phishing-detection",
            "--disable-default-apps",
            "--disable-hang-monitor",
            "--disable-popup-blocking",
            "--disable-prompt-on-repost",
            "--disable-sync",
            "--disable-translate",
            "--metrics-recording-only",
            "--no-first-run",
            "--safebrowsing-disable-auto-update",
        ]
        
        if not self.settings.usar_perfil_chrome:
            args.append("--profile-directory=Default")
        
        # Inicia o Chrome
        logger.info(f"Iniciando Chrome na porta {porta_debug}...")
        self._chrome_process = subprocess.Popen(
            args,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL
        )
        
        # Aguarda o Chrome iniciar
        time.sleep(3)
        
        # Conecta via CDP
        try:
            self.browser = self.playwright.chromium.connect_over_cdp(
                f"http://localhost:{porta_debug}"
            )
            self.context = self.browser.contexts[0] if self.browser.contexts else self.browser.new_context()
            self.page = self.context.pages[0] if self.context.pages else self.context.new_page()
            self.page.set_default_timeout(self.settings.timeout)
            
            logger.info("Conectado ao Chrome do sistema com sucesso")
            
        except Exception as e:
            logger.error(f"Erro ao conectar ao Chrome: {e}")
            self._chrome_process.terminate()
            self._iniciar_chromium_embutido()
    
    def _iniciar_chromium_embutido(self) -> None:
        """Inicia o Chromium embutido do Playwright com stealth."""
        logger.info("Usando Chromium embutido do Playwright...")
        
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
            try:
                self.context.close()
            except:
                pass
        if self.browser:
            try:
                self.browser.close()
            except:
                pass
        if self.playwright:
            self.playwright.stop()
        
        # Encerra processo do Chrome se estiver usando sistema
        if hasattr(self, '_chrome_process') and self._chrome_process:
            try:
                self._chrome_process.terminate()
            except:
                pass
        
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
            self.page.goto(url_ssg)
            
            # 2. Aguarda verificação de segurança (Cloudflare)
            print("   ⏳ Verificação Cloudflare...")
            
            logger.info("Aguardando verificação de segurança (Cloudflare)...")
            
            # Aguarda até que a URL mude para o portal de login
            url_login = "https://portal.sysmap.com.br/wp-login.php"
            self.page.wait_for_url(
                lambda url: "portal.sysmap.com.br" in url or "wp-login" in url,
                timeout=120000  # 2 minutos para verificação
            )
            
            # 3. Aguarda a página de login carregar
            self.page.wait_for_load_state("networkidle")
            logger.info(f"Página de login carregada: {self.page.url}")
            
            # 4. Preenche credenciais
            logger.info("Preenchendo credenciais...")
            
            # Aguarda o campo de usuário estar visível
            self.page.wait_for_selector(
                '#user_login',
                state="visible",
                timeout=30000
            )
            
            # Preenche usuário
            campo_usuario = self.page.locator('#user_login')
            campo_usuario.fill(self.settings.username)
            
            # Preenche senha
            campo_senha = self.page.locator('#user_pass')
            campo_senha.fill(self.settings.password)
            
            logger.info("Credenciais preenchidas")
            
            # 5. Preenche o código 2FA (automático ou manual)
            try:
                self.page.wait_for_selector('#googleotp', state="visible", timeout=10000)
                campo_2fa = self.page.locator('#googleotp')
                campo_2fa.focus()
                
                # Verifica se tem secret key configurada para 2FA automático
                if self.settings.totp_secret:
                    try:
                        import pyotp
                        totp = pyotp.TOTP(self.settings.totp_secret)
                        codigo_2fa = totp.now()
                        
                        print(f"   🔐 2FA automático: {codigo_2fa}")
                        logger.info(f"Preenchendo código 2FA automaticamente")
                        
                        campo_2fa.fill(codigo_2fa)
                        self.page.wait_for_timeout(500)
                        
                        # Clica no botão de login
                        botao_login = self.page.locator(
                            'input[type="submit"], button[type="submit"], input[name="wp-submit"]'
                        ).first
                        botao_login.click()
                        
                    except ImportError:
                        logger.warning("pyotp não instalado - 2FA manual")
                        print("   🔐 Digite o código 2FA no navegador...")
                    except Exception as e:
                        logger.warning(f"Erro ao gerar código TOTP: {e}")
                        print("   🔐 Digite o código 2FA no navegador...")
                else:
                    print("   🔐 Digite o código 2FA no navegador...")
                    logger.info("Foco no campo 2FA - aguardando preenchimento manual")
                    
            except Exception as e:
                logger.warning(f"Campo 2FA não encontrado: {e}")
                # Se não encontrar o campo 2FA, clica no botão de login
                botao_login = self.page.locator(
                    'input[type="submit"], button[type="submit"], input[name="wp-submit"]'
                ).first
                botao_login.click()
            
            # 6. Aguarda usuário completar validação (2FA)
            logger.info("Aguardando usuário completar validação...")
            
            # Aguarda até que o login seja finalizado (URL muda para portal principal)
            self.page.wait_for_url(
                lambda url: url == "https://portal.sysmap.com.br/" or 
                           (url.startswith("https://portal.sysmap.com.br") and "wp-login" not in url and "wp-admin" not in url),
                timeout=300000  # 5 minutos para completar login
            )
            
            # 7. Navega para a página de timesheet
            logger.info(f"Navegando para timesheet: {self.settings.timesheet_url}")
            self.page.goto(self.settings.timesheet_url)
            self.page.wait_for_load_state("networkidle")
            
            logger.info("Login realizado com sucesso")
            return True
            
        except Exception as e:
            logger.error(f"Erro ao realizar login: {e}")
            return False
    
    def selecionar_mes_e_filtrar(self, periodo: str = "mes_atual") -> bool:
        """
        Seleciona o período (mês atual ou mês passado) e clica em filtrar.
        
        Args:
            periodo: "mes_atual" para mês atual (li[1]) ou "mes_passado" para mês passado (li[2]).
        
        Returns:
            True se a operação foi bem sucedida, False caso contrário.
        """
        if not self.settings.selecionar_mes_atual:
            return True
        
        label_periodo = "mês passado" if periodo == "mes_passado" else "mês atual"
        logger.info(f"Selecionando {label_periodo} e filtrando...")
        
        try:
            # Aguarda página carregar
            self.page.wait_for_load_state("networkidle")
            
            # 1. Clica no dropdown de período
            dropdown_periodo = self.page.locator('xpath=/html/body/div[3]/div[2]/div[2]/div[3]/div/div/div/a[2]')
            dropdown_periodo.wait_for(state="visible", timeout=10000)
            dropdown_periodo.click()
            
            # 2. Seleciona a opção conforme o período
            # li[1] = Mês Atual, li[2] = Mês Passado
            indice_opcao = 2 if periodo == "mes_passado" else 1
            xpath_opcao = f'xpath=/html/body/div[3]/div[2]/div[2]/div[3]/div/div/div/ul/li[{indice_opcao}]/a'
            opcao_periodo = self.page.locator(xpath_opcao)
            opcao_periodo.wait_for(state="visible", timeout=5000)
            opcao_periodo.click()
            
            # 3. Clica no botão Pesquisar
            botao_pesquisar = self.page.locator('#ButtonSearch')
            botao_pesquisar.wait_for(state="visible", timeout=5000)
            botao_pesquisar.click()
            
            # Aguarda resultados carregarem
            self.page.wait_for_load_state("networkidle")
            
            logger.info(f"{label_periodo.capitalize()} selecionado e filtrado")
            return True
            
        except Exception as e:
            logger.error(f"Erro ao selecionar {label_periodo}: {e}")
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
            
            # Aguarda a tabela carregar
            self.page.wait_for_selector('#TableTimesheet', state="visible", timeout=10000)
            
            # Busca as datas nos inputs com classe activity-timesheet que têm atributo date
            # A data está no atributo "date" dos inputs de apontamento
            inputs_activity = self.page.locator('#TableTimesheet input.activity-timesheet[date]').all()
            
            for inp in inputs_activity:
                try:
                    data = inp.get_attribute('date')
                    if data and '/' in data:
                        datas.add(data.strip())
                except Exception as e:
                    logger.debug(f"Erro ao ler input: {e}")
                    continue
            
            self.datas_cadastradas = datas
            logger.info(f"Encontradas {len(datas)} datas já cadastradas: {datas}")
            
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
            # Clica no botão de adicionar novo registro (ícone +)
            botao_adicionar = self.page.locator('xpath=/html/body/div[3]/div[3]/div[1]/h3/span/i[2]')
            botao_adicionar.wait_for(state="visible", timeout=5000)
            botao_adicionar.click()
            
            # Aguarda a nova linha ser adicionada
            self.page.wait_for_timeout(500)
            
            logger.info("Botão de adicionar registro clicado")
            return True
            
        except Exception as e:
            logger.error(f"Erro ao clicar em adicionar registro: {e}")
            return False
    
    def obter_ultima_linha_tabela(self) -> int:
        """
        Obtém o índice da última linha da tabela (linha recém adicionada).
        
        Returns:
            Índice da última linha visível.
        """
        # Conta linhas dinâmicas visíveis (excluindo template)
        linhas = self.page.locator('#TableTimesheet tbody tr.dynamic[style*="display: table-row"]').count()
        # A linha recém adicionada está no índice: template(1) + linhas existentes + 1
        return linhas + 1
    
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
            # 1. Clica no botão de adicionar novo registro
            if not self.clicar_adicionar_registro():
                return False, []
            
            # Aguarda a linha ser criada
            self.page.wait_for_timeout(800)
            
            # 2. Obtém o índice da nova linha (última linha adicionada)
            idx_linha = self.obter_ultima_linha_tabela()
            logger.info(f"Nova linha adicionada no índice: {idx_linha}")
            
            # 3. Preenche a data na primeira coluna
            xpath_input_data = f'//*[@id="TableTimesheet"]/tbody/tr[{idx_linha}]/td[1]/div/input'
            input_data = self.page.locator(f'xpath={xpath_input_data}')
            input_data.wait_for(state="visible", timeout=5000)
            input_data.click()
            self.page.wait_for_timeout(100)
            input_data.fill("")  # Limpa o campo
            input_data.type(registro_ajustado.data, delay=50)  # Digita caractere por caractere
            input_data.press("Tab")  # Sai do campo para confirmar
            self.page.wait_for_timeout(500)
            
            # 4. Monta lista de pares de horários (entrada, saída) para preencher
            # Cada par de horário representa uma linha E-S
            # Verifica se tem almoço (4 horários) ou apenas entrada/saída (2 horários)
            if registro_ajustado.saida_almoco and registro_ajustado.retorno_almoco:
                # Registro completo com almoço
                pares_horarios = [
                    (registro_ajustado.entrada, registro_ajustado.saida_almoco),
                    (registro_ajustado.retorno_almoco, registro_ajustado.saida)
                ]
            else:
                # Registro simples: apenas entrada e saída (sem almoço)
                pares_horarios = [
                    (registro_ajustado.entrada, registro_ajustado.saida)
                ]
            
            # Dropdown para adicionar registros E-S adicionais
            xpath_dropdown = f'//*[@id="TableTimesheet"]/tbody/tr[{idx_linha}]/td[1]/div/div/button'
            xpath_opcao_es = f'//*[@id="TableTimesheet"]/tbody/tr[{idx_linha}]/td[1]/div/div/ul/li[1]/a'
            dropdown = self.page.locator(f'xpath={xpath_dropdown}')
            opcao_es = self.page.locator(f'xpath={xpath_opcao_es}')
            
            # 5. Preenche cada par de horários, adicionando linhas E-S conforme necessário
            for idx_par, (hora_entrada, hora_saida) in enumerate(pares_horarios):
                # Formata horários garantindo 2 dígitos (ex: 08:00)
                hora_entrada_fmt = formatar_horario(hora_entrada)
                hora_saida_fmt = formatar_horario(hora_saida)
                
                # A primeira linha E-S já vem pronta, as demais precisam ser adicionadas
                if idx_par > 0:
                    # Adiciona nova linha E-S via dropdown
                    dropdown.click()
                    self.page.wait_for_timeout(200)
                    opcao_es.click()
                    self.page.wait_for_timeout(500)
                
                # Atualiza lista de linhas após possível adição
                linhas_clock = self.page.locator(f'#TableTimesheet tbody tr.dynamic:nth-child({idx_linha}) table.table-clockInOut tbody tr.dynamicClockInOut').all()
                
                # Preenche a linha E-S correspondente
                if len(linhas_clock) > idx_par:
                    linha_clock = linhas_clock[idx_par]
                    
                    # Seleciona "ATIVIDADE EXTERNA" no select
                    select_tipo = linha_clock.locator('select.ddl-access-type')
                    if select_tipo.count() > 0:
                        select_tipo.select_option(value="ATIVIDADE EXTERNA")
                        self.page.wait_for_timeout(200)
                    
                    # Preenche entrada (clockin) - click, limpa e digita
                    input_entrada = linha_clock.locator('input.textbox-clockin')
                    if input_entrada.count() > 0:
                        input_entrada.click()
                        input_entrada.fill("")
                        input_entrada.type(hora_entrada_fmt, delay=30)
                        self.page.wait_for_timeout(100)
                    
                    # Preenche saída (clockout) - click, limpa e digita
                    input_saida = linha_clock.locator('input.textbox-clockout')
                    if input_saida.count() > 0:
                        input_saida.click()
                        input_saida.fill("")
                        input_saida.type(hora_saida_fmt, delay=30)
                        self.page.wait_for_timeout(100)
                    
                    logger.debug(f"Preenchido par {idx_par + 1}: {hora_entrada_fmt} - {hora_saida_fmt}")
            
            # 6. Calcula total de horas trabalhadas
            horas_trabalhadas = self._calcular_horas_trabalhadas(
                registro_ajustado.entrada,
                registro_ajustado.saida_almoco,
                registro_ajustado.retorno_almoco,
                registro_ajustado.saida
            )
            logger.info(f"Horas trabalhadas calculadas: {horas_trabalhadas}")
            
            # 7. Preenche as horas apontadas na tabela de apontamento
            # O ID da linha é baseado no índice (0, 1, 2...), que corresponde a idx_linha - 2
            # (pois idx_linha começa em 2: template=1, primeira linha=2)
            id_linha_apontamento = idx_linha - 2
            xpath_input_horas = f'//*[@id="{id_linha_apontamento}"]/td[2]/input'
            input_horas = self.page.locator(f'xpath={xpath_input_horas}')
            
            if input_horas.count() > 0:
                input_horas.click()
                input_horas.fill("")
                input_horas.type(horas_trabalhadas, delay=30)
                self.page.wait_for_timeout(300)
                logger.debug(f"Horas preenchidas no id={id_linha_apontamento}: {horas_trabalhadas}")
            else:
                # Fallback: tenta localizar dentro da linha específica
                linhas_timesheet = self.page.locator(f'#TableTimesheet tbody tr.dynamic:nth-child({idx_linha}) table.table-timesheetrecording tbody tr.dynamicTimesheetrecording').all()
                if linhas_timesheet:
                    linha_apontamento = linhas_timesheet[-1]
                    input_horas_alt = linha_apontamento.locator('td:nth-child(2) input')
                    if input_horas_alt.count() > 0:
                        input_horas_alt.click()
                        input_horas_alt.fill("")
                        input_horas_alt.type(horas_trabalhadas, delay=30)
                        self.page.wait_for_timeout(300)
                        logger.debug(f"Horas preenchidas (fallback): {horas_trabalhadas}")
            
            # 8. Clica no botão para selecionar OSI/Projeto/Atividade
            # Busca a linha de apontamento (timesheetrecording)
            linhas_timesheet = self.page.locator(f'#TableTimesheet tbody tr.dynamic:nth-child({idx_linha}) table.table-timesheetrecording tbody tr.dynamicTimesheetrecording').all()
            
            if linhas_timesheet:
                linha_apontamento = linhas_timesheet[-1]
                
                botao_selecionar_osi = linha_apontamento.locator('span.input-group-btn button.button-show-items')
                if botao_selecionar_osi.count() > 0:
                    botao_selecionar_osi.click()
                    self.page.wait_for_timeout(500)
                    
                    # 9. Aguarda o modal aparecer e clica na opção do projeto
                    botao_projeto = self.page.locator('xpath=/html/body/div[7]/div/div/div[2]/table/tbody/tr[2]/td[1]/button/i')
                    botao_projeto.wait_for(state="visible", timeout=5000)
                    botao_projeto.click()
                    self.page.wait_for_timeout(500)
            
            # Registra horários utilizados no validador
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
    
    def _calcular_horas_trabalhadas(self, entrada: str, saida_almoco: str, retorno_almoco: str, saida: str) -> str:
        """
        Calcula o total de horas trabalhadas no dia.
        
        Args:
            entrada: Horário de entrada (HH:MM)
            saida_almoco: Horário de saída para almoço (HH:MM) - pode ser vazio
            retorno_almoco: Horário de retorno do almoço (HH:MM) - pode ser vazio
            saida: Horário de saída (HH:MM)
            
        Returns:
            Total de horas no formato HH:MM
        """
        try:
            def horario_para_minutos(horario: str) -> int:
                h, m = map(int, horario.split(':'))
                return h * 60 + m
            
            # Verifica se tem horários de almoço
            if saida_almoco and retorno_almoco:
                # Período da manhã
                minutos_manha = horario_para_minutos(saida_almoco) - horario_para_minutos(entrada)
                
                # Período da tarde
                minutos_tarde = horario_para_minutos(saida) - horario_para_minutos(retorno_almoco)
                
                # Total
                total_minutos = minutos_manha + minutos_tarde
            else:
                # Registro simples: apenas entrada e saída
                total_minutos = horario_para_minutos(saida) - horario_para_minutos(entrada)
            
            horas = total_minutos // 60
            minutos = total_minutos % 60
            
            return f"{horas:02d}:{minutos:02d}"
            
        except Exception as e:
            logger.error(f"Erro ao calcular horas trabalhadas: {e}")
            return "08:00"  # Valor padrão em caso de erro
    
    def confirmar_apontamentos(self) -> bool:
        """
        Clica no botão de confirmar/salvar todos os apontamentos.
        
        Returns:
            True se confirmou com sucesso, False caso contrário.
        """
        logger.info("Confirmando apontamentos...")
        
        try:
            # Clica no botão de salvar (ícone de check/confirmar)
            botao_salvar = self.page.locator('xpath=/html/body/div[3]/div[3]/div[1]/h3/span/i[3]')
            botao_salvar.wait_for(state="visible", timeout=5000)
            botao_salvar.click()
            
            # Aguarda processamento
            self.page.wait_for_load_state("networkidle")
            self.page.wait_for_timeout(1000)
            
            logger.info("Apontamentos confirmados com sucesso")
            return True
            
        except Exception as e:
            logger.error(f"Erro ao confirmar apontamentos: {e}")
            return False
    
    def fechar_modal_confirmacao(self) -> bool:
        """
        Aguarda o usuário clicar no botão OK do modal de confirmação.
        
        Returns:
            True se o modal foi fechado, False caso contrário.
        """
        logger.info("Aguardando usuário confirmar no modal...")
        
        try:
            # Aguarda o botão OK aparecer no modal
            botao_ok = self.page.locator('xpath=/html/body/div[9]/div/div/div[2]/button')
            botao_ok.wait_for(state="visible", timeout=30000)
            
            logger.info("Modal exibido - aguardando usuário clicar em OK...")
            
            # Aguarda o botão desaparecer (usuário clicou)
            botao_ok.wait_for(state="hidden", timeout=300000)  # 5 minutos
            
            self.page.wait_for_timeout(500)
            
            logger.info("Modal de confirmação fechado pelo usuário")
            return True
            
        except Exception as e:
            logger.error(f"Erro ao aguardar modal de confirmação: {e}")
            return False
    
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
