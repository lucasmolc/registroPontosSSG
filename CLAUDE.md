# Instruções do projeto — Registro Automático de Pontos SSG

Aplicativo Windows (.NET 10 + WPF) que automatiza o registro de pontos no SSG da Sysmap,
distribuído como um `.exe` único self-contained. **Só existe a implementação .NET** — a
versão em Python foi removida; não recriar scripts de automação no repositório.

## Estrutura

| Caminho | Conteúdo |
| ------- | -------- |
| `src/RegistroPontosSSG.Core/` | Lógica sem UI: automação Playwright, leitura de planilhas, validação de horários, DPAPI, TOTP, atualização |
| `src/RegistroPontosSSG.Desktop/` | Aplicação WPF (MVVM com CommunityToolkit.Mvvm) |
| `tests/RegistroPontosSSG.Core.Tests/` | xUnit |
| `docs/` | Documentação de desenvolvimento e design system |
| `CHANGELOG.md` | **Lido pelo próprio app** (ver abaixo) |

Comandos: `dotnet build`, `dotnet test`, e para distribuir
`dotnet publish src\RegistroPontosSSG.Desktop\RegistroPontosSSG.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish`.

## Release e changelog — obrigatório

**Sempre que houver alterações substanciais, gere uma nova release e atualize o
`CHANGELOG.md` de acordo.** O app avisa o usuário quando há versão nova e mostra o resumo
do changelog na primeira execução após atualizar; um changelog desatualizado ou uma
alteração sem release significa que ninguém recebe a correção.

### O que conta como substancial

Gere release quando a mudança afeta quem usa o aplicativo:

- correção de bug que impedia ou distorcia o registro de pontos;
- adaptação a mudanças na interface do SSG (seletores, fluxo de login, filtros);
- suporte a novo formato de arquivo de pontos ou mudança nas regras de horário;
- nova funcionalidade ou alteração visível na interface;
- correção de falha de inicialização, de atualização ou de segurança.

Não gere release para: refatoração sem efeito observável, ajustes de documentação,
mudanças só em testes, ou alterações de workflow do CI.

Em dúvida, pergunte ao usuário antes de criar a tag — publicar release é ação externa.

### Passos

1. **Atualize o `CHANGELOG.md`**: adicione uma seção no topo, acima da anterior.
2. **Alinhe a versão no csproj**: `<Version>` em
   `src/RegistroPontosSSG.Desktop/RegistroPontosSSG.Desktop.csproj` deve ser a nova versão.
   O `release.yml` injeta a versão da tag no publish, mas o csproj é o padrão dos builds
   locais e a referência de "versão em desenvolvimento".
3. **Rode `dotnet test`** — o `release.yml` também roda, e uma release que falha nos testes
   não é publicada.
4. **Commit** com as duas alterações (changelog + csproj) junto do código.
5. **Tag e push**: `git tag vX.Y.Z && git push origin vX.Y.Z`. O workflow compila, testa,
   publica o `.exe` com a versão gravada e cria a GitHub Release com o executável anexado.

Numeração (semver simplificado): **PATCH** para correções, **MINOR** para novas
funcionalidades ou adaptações relevantes, **MAJOR** só em mudança que quebre o uso atual.

### Formato do CHANGELOG

O arquivo é **parseado pelo app** (`ChangelogReader`), então o formato do cabeçalho não é
livre — use exatamente:

```markdown
## [1.4.0] - 2026-08-15

### Adicionado
- Frase curta, no que muda para quem usa.

### Corrigido
- Outra frase curta.
```

Regras:

- Cabeçalho `## [X.Y.Z] - AAAA-MM-DD`. A data é a do lançamento; converta datas relativas.
- Subseções: `### Adicionado`, `### Corrigido`, `### Alterado`, `### Removido`.
- **O texto é exibido ao usuário final dentro do app.** Escreva para quem usa, não para
  quem programa: descreva o efeito observável, não a implementação. Prefira "o relatório
  exportado do SSG era lido como vazio" a "janela de varredura de 5 linhas no
  PunchFileReader".
- Sem nomes de classe, caminhos de código ou números de commit. Detalhe técnico vai na
  mensagem de commit.

## Convenções do código

- Comentários e mensagens de log em português; identificadores em inglês, seguindo o código
  existente.
- Seletores da interface do SSG ficam centralizados em
  `src/RegistroPontosSSG.Core/Automation/SsgSelectors.cs`. Mudança na tela do SSG começa por
  lá — não espalhar seletor pelo código nem usar XPath posicional.
- Configuração, logs e perfil do Chrome ficam em `%APPDATA%\RegistroPontosSSG\`, **fora da
  pasta do executável** — é isso que preserva os dados do usuário quando o `.exe` é
  substituído numa atualização. Não gravar estado junto do executável.
- Senha e secret TOTP são cifrados via DPAPI (`ProtectedStorage`). Nunca gravar, registrar
  em log ou imprimir esses valores em texto puro.
- Campos mascarados da SPA do SSG (`.mask-time`) ignoram `FillAsync`: preencher digitando os
  dígitos (ver `FillMaskedFieldAsync`).
- Recurso XAML inexistente ou arquivo não declarado como `<Resource>` derruba a janela na
  inicialização. Após mexer na UI, execute o `.exe` e confirme que a janela abre.

## Verificação antes de concluir

- `dotnet build` e `dotnet test` sem erros.
- Mexeu na interface? Abra o aplicativo e confirme que a janela principal aparece
  (falhas de XAML só se manifestam em execução, e o handler grava
  `%APPDATA%\RegistroPontosSSG\logs\crash-*.log`).
- Mexeu na automação do SSG? O fluxo real depende de login com 2FA e Cloudflare; valide no
  navegador e não confie apenas na compilação.
- Alteração substancial? Changelog atualizado e release criada, conforme a política acima.
