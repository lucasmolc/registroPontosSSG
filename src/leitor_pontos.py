"""
Módulo responsável pela leitura do arquivo de pontos.
"""
import re
from pathlib import Path
from typing import List, Dict, Any, Optional
from dataclasses import dataclass
from datetime import datetime, time

import pandas as pd
from loguru import logger


@dataclass
class RegistroPonto:
    """Representa um registro de ponto."""
    data: str
    entrada: str
    saida_almoco: str
    retorno_almoco: str
    saida: str
    observacao: str = ""
    
    def __str__(self) -> str:
        if self.tem_almoco():
            return f"Ponto {self.data}: {self.entrada} - {self.saida_almoco} | {self.retorno_almoco} - {self.saida}"
        return f"Ponto {self.data}: {self.entrada} - {self.saida}"
    
    def tem_almoco(self) -> bool:
        """Verifica se o registro tem horários de almoço preenchidos."""
        return bool(self.saida_almoco and self.retorno_almoco)
    
    def is_valido(self) -> bool:
        """
        Verifica se o registro tem os horários mínimos preenchidos.
        
        Aceita dois formatos:
        - Completo: entrada, saída almoço, retorno almoço, saída (4 horários)
        - Simples: apenas entrada e saída (2 horários)
        """
        # Sempre precisa de data, entrada e saída
        if not all([self.data, self.entrada, self.saida]):
            return False
        
        # Se tem almoço parcial (só um dos dois), é inválido
        if bool(self.saida_almoco) != bool(self.retorno_almoco):
            return False
        
        return True


class LeitorPontos:
    """Classe responsável por ler e processar arquivos de pontos."""
    
    def __init__(self, caminho_arquivo: Path, formato: str = "xlsx"):
        """
        Inicializa o leitor de pontos.
        
        Args:
            caminho_arquivo: Caminho para o arquivo de pontos.
            formato: Formato do arquivo (xlsx ou csv).
        """
        self.caminho_arquivo = Path(caminho_arquivo)
        self.formato = formato.lower()
        self._validar_arquivo()
    
    def _validar_arquivo(self) -> None:
        """Valida se o arquivo existe e tem o formato correto."""
        if not self.caminho_arquivo.exists():
            raise FileNotFoundError(f"Arquivo de pontos não encontrado: {self.caminho_arquivo}")
        
        if self.formato not in ["xlsx", "csv"]:
            raise ValueError(f"Formato de arquivo não suportado: {self.formato}")
    
    def ler_pontos(self) -> List[RegistroPonto]:
        """
        Lê o arquivo de pontos e retorna uma lista de registros.
        
        Returns:
            Lista de objetos RegistroPonto.
        """
        logger.info(f"Lendo arquivo de pontos: {self.caminho_arquivo}")
        
        try:
            if self.formato == "xlsx":
                df = pd.read_excel(self.caminho_arquivo, header=None)
            else:
                df = pd.read_csv(self.caminho_arquivo, encoding="utf-8", header=None)
            
            # Detecta o formato do arquivo
            if self._is_formato_ssg_report(df):
                logger.info("Formato detectado: Relatório SSG")
                return self._processar_formato_ssg(df)
            else:
                logger.info("Formato detectado: Planilha padrão")
                return self._processar_formato_padrao(df)
        
        except Exception as e:
            logger.error(f"Erro ao ler arquivo de pontos: {e}")
            raise
    
    def _is_formato_ssg_report(self, df: pd.DataFrame) -> bool:
        """Verifica se o arquivo está no formato de relatório do SSG."""
        # Procura por padrões típicos do relatório SSG
        for i in range(min(5, len(df))):
            for j in range(min(3, len(df.columns))):
                valor = str(df.iloc[i, j]) if pd.notna(df.iloc[i, j]) else ""
                if "Punch in" in valor or "Date" in valor or re.match(r"(Mon|Tue|Wed|Thu|Fri|Sat|Sun),", valor):
                    return True
        return False
    
    def _processar_formato_ssg(self, df: pd.DataFrame) -> List[RegistroPonto]:
        """
        Processa arquivo no formato de relatório SSG.
        
        O formato SSG tem:
        - Linha com data: "Mon, 05/01/26" na coluna 0
        - Horários na coluna 1 (Punch in) e coluna 2 (Punch out)
        - Se há almoço, segunda linha de horários logo abaixo
        
        Args:
            df: DataFrame com os dados.
            
        Returns:
            Lista de objetos RegistroPonto.
        """
        registros = []
        i = 0
        
        while i < len(df):
            # Procura linha com data (formato: "Day, DD/MM/YY")
            valor_col0 = str(df.iloc[i, 0]) if pd.notna(df.iloc[i, 0]) else ""
            
            # Padrão: "Mon, 05/01/26" ou similar
            match_data = re.match(r"(Mon|Tue|Wed|Thu|Fri|Sat|Sun),\s*(\d{2}/\d{2}/\d{2})", valor_col0)
            
            if match_data:
                data_str = match_data.group(2)  # Ex: "05/01/26"
                
                # Converte para formato DD/MM/YYYY
                try:
                    data_obj = datetime.strptime(data_str, "%d/%m/%y")
                    data_formatada = data_obj.strftime("%d/%m/%Y")
                except:
                    data_formatada = data_str
                
                # Verifica se tem horários na mesma linha
                punch_in_1 = self._extrair_horario(df.iloc[i, 1])
                punch_out_1 = self._extrair_horario(df.iloc[i, 2])
                
                # Se não tem horários, pula (dia sem registro)
                if not punch_in_1:
                    logger.debug(f"Data {data_formatada} sem horários - ignorando")
                    i += 1
                    continue
                
                # Verifica linha seguinte para segundo par de horários (almoço)
                punch_in_2 = None
                punch_out_2 = None
                
                if i + 1 < len(df):
                    valor_prox_col0 = str(df.iloc[i + 1, 0]) if pd.notna(df.iloc[i + 1, 0]) else ""
                    # Se a próxima linha NÃO tem data, pode ter horários adicionais
                    if not re.match(r"(Mon|Tue|Wed|Thu|Fri|Sat|Sun),", valor_prox_col0):
                        punch_in_2 = self._extrair_horario(df.iloc[i + 1, 1])
                        punch_out_2 = self._extrair_horario(df.iloc[i + 1, 2])
                
                # Monta o registro
                if punch_in_2 and punch_out_2:
                    # Tem almoço: entrada, saída almoço, retorno almoço, saída
                    registro = RegistroPonto(
                        data=data_formatada,
                        entrada=punch_in_1,
                        saida_almoco=punch_out_1,
                        retorno_almoco=punch_in_2,
                        saida=punch_out_2
                    )
                else:
                    # Sem almoço: só entrada e saída
                    registro = RegistroPonto(
                        data=data_formatada,
                        entrada=punch_in_1,
                        saida_almoco="",
                        retorno_almoco="",
                        saida=punch_out_1
                    )
                
                # Só adiciona se tiver dados válidos completos
                if registro.is_valido():
                    registros.append(registro)
                    logger.debug(f"Registro processado: {registro}")
                else:
                    logger.debug(f"Registro incompleto ignorado: {data_formatada}")
            
            i += 1
        
        logger.info(f"Total de registros válidos lidos: {len(registros)}")
        return registros
    
    def _extrair_horario(self, valor: Any) -> Optional[str]:
        """
        Extrai horário de um valor, retornando no formato HH:MM.
        
        Args:
            valor: Valor a processar.
            
        Returns:
            Horário no formato HH:MM ou None se inválido.
        """
        if pd.isna(valor):
            return None
        
        valor_str = str(valor).strip()
        
        # Ignora mensagens de erro
        if "No time punches" in valor_str or not valor_str:
            return None
        
        # Tenta extrair horário no formato HH:MM
        match = re.search(r"(\d{1,2}):(\d{2})", valor_str)
        if match:
            hora = int(match.group(1))
            minuto = int(match.group(2))
            return f"{hora:02d}:{minuto:02d}"
        
        # Se for objeto time
        if isinstance(valor, time):
            return valor.strftime("%H:%M")
        
        # Se for datetime
        if isinstance(valor, datetime):
            return valor.strftime("%H:%M")
        
        return None
    
    def _processar_formato_padrao(self, df: pd.DataFrame) -> List[RegistroPonto]:
        """
        Processa DataFrame no formato padrão (planilha simples).
        
        Args:
            df: DataFrame com os dados dos pontos.
            
        Returns:
            Lista de objetos RegistroPonto.
        """
        registros = []
        
        # Tenta encontrar o header
        header_row = None
        for i in range(min(5, len(df))):
            row_values = [str(v).lower() if pd.notna(v) else "" for v in df.iloc[i]]
            if any("data" in v or "date" in v for v in row_values):
                header_row = i
                break
        
        if header_row is not None:
            df.columns = df.iloc[header_row]
            df = df.iloc[header_row + 1:].reset_index(drop=True)
        
        # Normaliza nomes das colunas
        df.columns = [str(c).lower().strip() if pd.notna(c) else f"col_{i}" for i, c in enumerate(df.columns)]
        
        # Mapeamento de colunas esperadas
        colunas_esperadas = {
            "data": ["data", "date", "dia"],
            "entrada": ["entrada", "entry", "inicio", "entrada_manha", "punch in"],
            "saida_almoco": ["saida_almoco", "saida almoco", "almoco_saida"],
            "retorno_almoco": ["retorno_almoco", "retorno almoco", "almoco_retorno", "entrada_tarde"],
            "saida": ["saida", "exit", "fim", "saida_tarde", "punch out"],
            "observacao": ["observacao", "obs", "observation", "nota"]
        }
        
        # Encontra as colunas no DataFrame
        mapeamento = {}
        for campo, possiveis in colunas_esperadas.items():
            for possivel in possiveis:
                if possivel in df.columns:
                    mapeamento[campo] = possivel
                    break
        
        logger.debug(f"Mapeamento de colunas: {mapeamento}")
        
        for _, row in df.iterrows():
            try:
                data = self._formatar_data(row.get(mapeamento.get("data", "data"), ""))
                entrada = self._formatar_hora(row.get(mapeamento.get("entrada", "entrada"), ""))
                saida_almoco = self._formatar_hora(row.get(mapeamento.get("saida_almoco", "saida_almoco"), ""))
                retorno_almoco = self._formatar_hora(row.get(mapeamento.get("retorno_almoco", "retorno_almoco"), ""))
                saida = self._formatar_hora(row.get(mapeamento.get("saida", "saida"), ""))
                observacao = str(row.get(mapeamento.get("observacao", "observacao"), "")) if pd.notna(row.get(mapeamento.get("observacao", "observacao"), "")) else ""
                
                registro = RegistroPonto(
                    data=data,
                    entrada=entrada,
                    saida_almoco=saida_almoco,
                    retorno_almoco=retorno_almoco,
                    saida=saida,
                    observacao=observacao
                )
                
                # Só adiciona se tiver dados válidos
                if registro.is_valido():
                    registros.append(registro)
                    logger.debug(f"Registro processado: {registro}")
                    
            except Exception as e:
                logger.warning(f"Erro ao processar linha: {e}")
                continue
        
        logger.info(f"Total de registros válidos lidos: {len(registros)}")
        return registros
    
    def _formatar_data(self, valor: Any) -> str:
        """Formata o valor da data para string."""
        if pd.isna(valor):
            return ""
        
        if isinstance(valor, datetime):
            return valor.strftime("%d/%m/%Y")
        
        return str(valor).strip()
    
    def _formatar_hora(self, valor: Any) -> str:
        """Formata o valor da hora para string."""
        if pd.isna(valor):
            return ""
        
        if isinstance(valor, time):
            return valor.strftime("%H:%M")
        
        if isinstance(valor, datetime):
            return valor.strftime("%H:%M")
        
        # Tenta extrair horário de string
        valor_str = str(valor).strip()
        match = re.search(r"(\d{1,2}):(\d{2})", valor_str)
        if match:
            hora = int(match.group(1))
            minuto = int(match.group(2))
            return f"{hora:02d}:{minuto:02d}"
        
        return valor_str
