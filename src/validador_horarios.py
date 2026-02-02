"""
Módulo de validação e ajuste automático de horários conforme regras do SSG.
"""
from datetime import datetime, timedelta
from typing import List, Tuple, Set, Dict
from dataclasses import dataclass

from loguru import logger


@dataclass
class RegrasValidacao:
    """Regras de validação de horários."""
    bloquear_horarios_redondos: bool = True
    dias_verificar_duplicados: int = 5
    bloquear_horarios_duplicados: bool = True
    bloquear_almoco_1_hora_exata: bool = True


@dataclass 
class RegistroAjustado:
    """Registro de ponto com horários ajustados."""
    data: str
    entrada: str
    saida_almoco: str
    retorno_almoco: str
    saida: str
    ajustes_realizados: List[str]
    
    def teve_ajustes(self) -> bool:
        return len(self.ajustes_realizados) > 0


class ValidadorHorarios:
    """Classe responsável por validar e ajustar horários conforme regras do SSG."""
    
    def __init__(self, regras: RegrasValidacao):
        """
        Inicializa o validador.
        
        Args:
            regras: Objeto com as regras de validação.
        """
        self.regras = regras
        self._horarios_utilizados: Dict[str, List[str]] = {}  # {data: [horarios]}
    
    def _adicionar_minutos(self, horario: str, minutos: int) -> str:
        """Adiciona minutos a um horário."""
        try:
            dt = datetime.strptime(horario, "%H:%M")
            dt = dt + timedelta(minutes=minutos)
            return dt.strftime("%H:%M")
        except ValueError:
            return horario
    
    def _is_horario_redondo(self, horario: str) -> bool:
        """Verifica se o horário é redondo (minutos = 00)."""
        try:
            minutos = int(horario.split(":")[1])
            return minutos == 0
        except (ValueError, IndexError):
            return False
    
    def _ajustar_horario_redondo(self, horario: str) -> Tuple[str, bool]:
        """
        Ajusta horário redondo adicionando +1 minuto.
        
        Returns:
            Tupla (horário ajustado, se foi ajustado).
        """
        if not self.regras.bloquear_horarios_redondos:
            return horario, False
        
        if self._is_horario_redondo(horario):
            # Adiciona 1 minuto
            novo_horario = self._adicionar_minutos(horario, 1)
            return novo_horario, True
        
        return horario, False
    
    def _get_tempo_almoco_minutos(self, saida_almoco: str, retorno_almoco: str) -> int:
        """Calcula o tempo de almoço em minutos."""
        try:
            saida = datetime.strptime(saida_almoco, "%H:%M")
            retorno = datetime.strptime(retorno_almoco, "%H:%M")
            diferenca = retorno - saida
            return diferenca.seconds // 60
        except ValueError:
            return 0
    
    def _ajustar_almoco_1_hora(self, saida_almoco: str, retorno_almoco: str) -> Tuple[str, bool]:
        """
        Ajusta retorno do almoço se for exatamente 1 hora.
        
        Returns:
            Tupla (horário de retorno ajustado, se foi ajustado).
        """
        if not self.regras.bloquear_almoco_1_hora_exata:
            return retorno_almoco, False
        
        minutos_almoco = self._get_tempo_almoco_minutos(saida_almoco, retorno_almoco)
        
        if minutos_almoco == 60:
            # Adiciona 1 minuto ao retorno
            novo_retorno = self._adicionar_minutos(retorno_almoco, 1)
            return novo_retorno, True
        
        return retorno_almoco, False
    
    def _is_horario_duplicado(self, data: str, horario: str) -> bool:
        """Verifica se o horário está duplicado nos últimos X dias."""
        if not self.regras.bloquear_horarios_duplicados:
            return False
        
        try:
            data_atual = datetime.strptime(data, "%d/%m/%Y")
            
            for data_str, horarios in self._horarios_utilizados.items():
                data_registro = datetime.strptime(data_str, "%d/%m/%Y")
                diferenca_dias = (data_atual - data_registro).days
                
                if 0 < diferenca_dias <= self.regras.dias_verificar_duplicados:
                    if horario in horarios:
                        return True
            
            return False
        except ValueError:
            return False
    
    def _ajustar_horario_duplicado(self, data: str, horario: str, horarios_ja_usados: List[str]) -> Tuple[str, bool]:
        """
        Ajusta horário duplicado adicionando minutos.
        
        Args:
            data: Data do registro.
            horario: Horário a verificar.
            horarios_ja_usados: Lista de horários já usados neste registro.
            
        Returns:
            Tupla (horário ajustado, se foi ajustado).
        """
        if not self.regras.bloquear_horarios_duplicados:
            return horario, False
        
        horario_atual = horario
        ajustado = False
        tentativas = 0
        max_tentativas = 30  # Evita loop infinito
        
        while tentativas < max_tentativas:
            # Verifica se está duplicado no histórico ou neste registro
            duplicado_historico = self._is_horario_duplicado(data, horario_atual)
            duplicado_registro = horario_atual in horarios_ja_usados
            
            if not duplicado_historico and not duplicado_registro:
                break
            
            # Adiciona 1 minuto
            horario_atual = self._adicionar_minutos(horario_atual, 1)
            ajustado = True
            tentativas += 1
        
        return horario_atual, ajustado
    
    def registrar_horarios_utilizados(self, data: str, horarios: List[str]) -> None:
        """
        Registra horários utilizados para verificação de duplicação.
        
        Args:
            data: Data do registro.
            horarios: Lista de horários utilizados.
        """
        if data not in self._horarios_utilizados:
            self._horarios_utilizados[data] = []
        self._horarios_utilizados[data].extend(horarios)
    
    def carregar_horarios_existentes(self, horarios_ssg: Dict[str, List[str]]) -> None:
        """
        Carrega horários já existentes no SSG.
        
        Args:
            horarios_ssg: Dicionário {data: [horarios]} do SSG.
        """
        self._horarios_utilizados.update(horarios_ssg)
        logger.debug(f"Carregados {len(horarios_ssg)} dias de horários existentes")
    
    def _ajustar_horario_completo(self, data: str, horario_original: str, 
                                    horarios_usados: List[str], nome_campo: str) -> Tuple[str, List[str]]:
        """
        Ajusta um horário aplicando todas as regras com verificação recursiva.
        
        Primeiro ajusta se for redondo (+1min), depois verifica duplicados.
        Se após ajuste de redondo gerar duplicado, continua ajustando +1min.
        
        Args:
            data: Data do registro.
            horario_original: Horário original.
            horarios_usados: Lista de horários já usados neste registro.
            nome_campo: Nome do campo para mensagens de ajuste.
            
        Returns:
            Tupla (horário ajustado, lista de ajustes realizados).
        """
        ajustes = []
        horario_atual = horario_original
        max_tentativas = 60  # Evita loop infinito
        tentativas = 0
        
        while tentativas < max_tentativas:
            precisa_ajuste = False
            motivo = ""
            
            # Verifica se é horário redondo
            if self.regras.bloquear_horarios_redondos and self._is_horario_redondo(horario_atual):
                precisa_ajuste = True
                motivo = "horário redondo"
            
            # Verifica se é duplicado no histórico
            elif self.regras.bloquear_horarios_duplicados and self._is_horario_duplicado(data, horario_atual):
                precisa_ajuste = True
                motivo = "duplicado histórico"
            
            # Verifica se é duplicado neste registro
            elif self.regras.bloquear_horarios_duplicados and horario_atual in horarios_usados:
                precisa_ajuste = True
                motivo = "duplicado no registro"
            
            if not precisa_ajuste:
                break
            
            # Adiciona +1 minuto
            horario_anterior = horario_atual
            horario_atual = self._adicionar_minutos(horario_atual, 1)
            
            # Registra apenas o primeiro ajuste com o motivo inicial
            if tentativas == 0:
                ajustes.append(f"{nome_campo}: {horario_original} → {{final}} ({motivo})")
            
            tentativas += 1
        
        # Atualiza a mensagem de ajuste com o horário final
        if ajustes:
            ajustes[0] = ajustes[0].replace("{final}", horario_atual)
        
        return horario_atual, ajustes
    
    def _calcular_minutos_trabalhados(self, entrada: str, saida_almoco: str, 
                                        retorno_almoco: str, saida: str) -> int:
        """
        Calcula o total de minutos trabalhados no dia.
        
        Args:
            entrada: Horário de entrada.
            saida_almoco: Horário de saída para almoço (pode ser vazio).
            retorno_almoco: Horário de retorno do almoço (pode ser vazio).
            saida: Horário de saída.
            
        Returns:
            Total de minutos trabalhados.
        """
        try:
            def horario_para_minutos(h: str) -> int:
                partes = h.split(":")
                return int(partes[0]) * 60 + int(partes[1])
            
            # Verifica se tem horários de almoço
            if saida_almoco and retorno_almoco:
                # Período da manhã
                manha = horario_para_minutos(saida_almoco) - horario_para_minutos(entrada)
                # Período da tarde
                tarde = horario_para_minutos(saida) - horario_para_minutos(retorno_almoco)
                return manha + tarde
            else:
                # Registro simples: apenas entrada e saída
                return horario_para_minutos(saida) - horario_para_minutos(entrada)
        except (ValueError, IndexError):
            return 0
    
    def ajustar_registro(self, data: str, entrada: str, saida_almoco: str, 
                         retorno_almoco: str, saida: str) -> RegistroAjustado:
        """
        Ajusta automaticamente um registro de ponto conforme as regras.
        
        Aplica +1min para horários redondos e duplicados, verificando
        recursivamente se o ajuste gerou novos conflitos.
        
        IMPORTANTE: O total de horas trabalhadas é mantido igual ao original.
        Se entrada ou retorno forem adiantados (+1min), a saída correspondente
        será compensada (+1min) para manter o mesmo total.
        
        Suporta registros com ou sem almoço:
        - Com almoço: 4 horários (entrada, saída almoço, retorno almoço, saída)
        - Sem almoço: 2 horários (entrada, saída)
        
        Args:
            data: Data do registro.
            entrada: Horário de entrada.
            saida_almoco: Horário de saída para almoço (pode ser vazio).
            retorno_almoco: Horário de retorno do almoço (pode ser vazio).
            saida: Horário de saída.
            
        Returns:
            RegistroAjustado com horários corrigidos.
        """
        ajustes = []
        horarios_usados = []  # Horários já usados neste registro
        
        # Verifica se tem horários de almoço
        tem_almoco = bool(saida_almoco and retorno_almoco)
        
        # Calcula total de minutos trabalhados ORIGINAL (antes de qualquer ajuste)
        minutos_originais = self._calcular_minutos_trabalhados(entrada, saida_almoco, retorno_almoco, saida)
        
        # Guarda horários originais para calcular compensação
        entrada_original = entrada
        saida_almoco_original = saida_almoco
        retorno_almoco_original = retorno_almoco
        saida_original = saida
        
        # 1. Ajusta entrada
        entrada, ajustes_entrada = self._ajustar_horario_completo(data, entrada, horarios_usados, "Entrada")
        ajustes.extend(ajustes_entrada)
        horarios_usados.append(entrada)
        
        # Processamento de horários de almoço (apenas se existirem)
        if tem_almoco:
            # 2. Ajusta saída almoço
            saida_almoco, ajustes_saida_almoco = self._ajustar_horario_completo(data, saida_almoco, horarios_usados, "Saída almoço")
            ajustes.extend(ajustes_saida_almoco)
            horarios_usados.append(saida_almoco)
            
            # 3. Ajusta retorno almoço (primeiro verifica almoço 1h exata)
            retorno_almoco_adj, adj_almoco = self._ajustar_almoco_1_hora(saida_almoco, retorno_almoco)
            if adj_almoco:
                ajustes.append(f"Retorno almoço: {retorno_almoco} → {retorno_almoco_adj} (almoço 1h exata)")
            retorno_almoco = retorno_almoco_adj
            
            # Depois verifica redondo/duplicado
            retorno_almoco, ajustes_retorno = self._ajustar_horario_completo(data, retorno_almoco, horarios_usados, "Retorno almoço")
            ajustes.extend(ajustes_retorno)
            horarios_usados.append(retorno_almoco)
        
        # 4. Calcula compensação necessária para manter total de horas
        # Calcula minutos trabalhados com os ajustes feitos (sem ajustar saída ainda)
        minutos_atuais = self._calcular_minutos_trabalhados(entrada, saida_almoco, retorno_almoco, saida_original)
        
        # Diferença = quanto precisa compensar na saída
        diferenca_minutos = minutos_originais - minutos_atuais
        
        # Ajusta saída para compensar a diferença
        if diferenca_minutos != 0:
            saida = self._adicionar_minutos(saida_original, diferenca_minutos)
            if diferenca_minutos > 0:
                ajustes.append(f"Saída: {saida_original} → {saida} (compensação +{diferenca_minutos}min)")
            else:
                ajustes.append(f"Saída: {saida_original} → {saida} (compensação {diferenca_minutos}min)")
        
        # 5. Ajusta saída por redondo/duplicado (e compensa se necessário)
        saida_antes_ajuste = saida
        saida, ajustes_saida = self._ajustar_horario_completo(data, saida, horarios_usados, "Saída")
        ajustes.extend(ajustes_saida)
        horarios_usados.append(saida)
        
        # Se saída foi ajustada por redondo/duplicado, compensa no retorno do almoço (apenas se tiver almoço)
        if tem_almoco and saida != saida_antes_ajuste:
            # Calcula quantos minutos a saída avançou
            minutos_avancados_saida = self._diferenca_minutos(saida_antes_ajuste, saida)
            
            if minutos_avancados_saida > 0:
                # Tenta compensar atrasando o retorno do almoço (aumentando almoço)
                # Mas precisa verificar se não gera duplicado
                retorno_compensado = self._adicionar_minutos(retorno_almoco, minutos_avancados_saida)
                
                # Verifica se o retorno compensado não gera conflito
                if not self._is_horario_duplicado(data, retorno_compensado) and retorno_compensado not in horarios_usados[:-1]:
                    # Remove retorno antigo e adiciona novo
                    horarios_usados.remove(retorno_almoco)
                    retorno_almoco = retorno_compensado
                    horarios_usados.insert(2, retorno_almoco)  # Insere na posição correta
                    ajustes.append(f"Retorno almoço: ajustado para {retorno_almoco} (compensação saída)")
        
        # Verifica se o total final está correto
        minutos_finais = self._calcular_minutos_trabalhados(entrada, saida_almoco, retorno_almoco, saida)
        if minutos_finais != minutos_originais:
            logger.warning(f"Atenção: horas trabalhadas alteradas de {minutos_originais}min para {minutos_finais}min")
        
        return RegistroAjustado(
            data=data,
            entrada=entrada,
            saida_almoco=saida_almoco,
            retorno_almoco=retorno_almoco,
            saida=saida,
            ajustes_realizados=ajustes
        )
    
    def _diferenca_minutos(self, horario1: str, horario2: str) -> int:
        """Calcula a diferença em minutos entre dois horários (horario2 - horario1)."""
        try:
            h1 = datetime.strptime(horario1, "%H:%M")
            h2 = datetime.strptime(horario2, "%H:%M")
            return int((h2 - h1).total_seconds() / 60)
        except ValueError:
            return 0
