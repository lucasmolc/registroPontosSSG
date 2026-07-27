# Changelog

Todas as mudanças relevantes do aplicativo. As seções abaixo são lidas pelo próprio
app: na primeira execução depois de uma atualização, o resumo da nova versão é exibido
automaticamente.

## [1.3.0] - 2026-07-27

### Adicionado
- Aviso de nova versão: o app consulta as releases do GitHub e mostra um alerta quando
  há atualização disponível, com o tamanho do download e link para as notas.
- Atualização automática: baixa o novo executável, substitui o arquivo e reabre o app.
  Suas configurações são preservadas — ficam em `%APPDATA%\RegistroPontosSSG\config.json`,
  fora da pasta do executável, e uma cópia de segurança é feita antes da troca.
- Resumo de novidades na primeira execução após atualizar.
- Suíte de testes automatizados (xUnit) cobrindo a leitura de planilhas e as regras de
  ajuste de horários, executada no CI e antes de cada release.

### Alterado
- A verificação de atualização pode ser desligada na aba Credenciais.

## [1.2.0] - 2026-07-27

### Corrigido
- Adaptação à nova interface do SSG. O sistema migrou para uma aplicação de página
  única e a tela antiga de apontamento deixou de existir, o que impedia qualquer
  registro. Todos os seletores foram refeitos sobre a nova tela de Registros de
  Entrada/Saída.
- Leitura do relatório exportado do SSG: o arquivo era lido como vazio porque o
  cabeçalho fica na sexta linha, fora da faixa que o leitor inspecionava.
- Planilha no formato padrão: a coluna de saída era confundida com a de saída para
  almoço, então o horário de saída do dia nunca era lido.
- Falha na inicialização causada pelo ícone da janela principal, que abria apenas uma
  caixa de diálogo de erro.
- Conexão com o Chrome usava `localhost`, resolvido como IPv6 no Windows e recusado
  pelo navegador; agora usa `127.0.0.1`.

### Adicionado
- Reaproveitamento de uma instância do Chrome já aberta em modo de depuração, em vez de
  subir outra sobre o mesmo perfil (o que impedia a conexão).
- Registro de falhas inesperadas em `%APPDATA%\RegistroPontosSSG\logs\crash-*.log`, com
  o rastreamento completo do erro.
- Aviso quando nenhum registro de ponto é reconhecido no arquivo selecionado.
- Workflows de build e release no GitHub Actions, publicando o executável a cada versão.

### Removido
- Implementação em Python, que duplicava a mesma automação. Apenas o aplicativo .NET é
  mantido.
