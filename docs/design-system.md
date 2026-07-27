# Design System — Registro Pontos SSG Desktop

> Documento normativo de UI/UX. Toda alteração visual deve respeitar estas regras.
> Em caso de novo padrão ainda não documentado, atualize este arquivo **na mesma operação** da implementação.

---

## 1. Princípios

1. **Dark-first** — a UI nasceu para tema escuro. Light mode não é suportado.
2. **Contraste elegante** — violet (`#8B5CF6`) como cor de marca, usada com parcimônia. Nunca saturar a tela de roxo.
3. **Hierarquia por elevação** — superfícies mais "altas" são mais claras (não sombras). Três níveis de superfície apenas.
4. **Sem decoração gratuita** — bordas finas (1px), cantos arredondados consistentes (8px/12px), zero sombras heavy, zero gradientes chamativos.
5. **Tipografia limpa** — Segoe UI Variable, escala de 4 tamanhos. Pesos: 400 / 500 / 600.
6. **Movimento sutil** — hover muda cor em 120ms. Sem bounce, sem easing exagerado.

---

## 2. Tokens

Todos os tokens vivem em `App.xaml` como `<SolidColorBrush x:Key="...">`. Nunca use cores hardcoded em XAML — sempre `{StaticResource Token}`.

### 2.1 Superfícies (escala de elevação)

| Token            | Hex        | Uso                                                    |
| ---------------- | ---------- | ------------------------------------------------------ |
| `Bg`             | `#0B0F1A`  | Background da janela (nível 0)                         |
| `Surface`        | `#131827`  | Cards, painéis principais (nível 1)                    |
| `SurfaceAlt`     | `#1B2236`  | Inputs, hover de listas, header da TabControl (nível 2) |
| `SurfaceHigh`    | `#252D45`  | Hover/seleção sobre SurfaceAlt (nível 3)               |

### 2.2 Bordas e divisores

| Token            | Hex        | Uso                                       |
| ---------------- | ---------- | ----------------------------------------- |
| `Border`         | `#252D45`  | Borda padrão (cards, inputs)              |
| `BorderStrong`   | `#374160`  | Borda em foco/hover                       |
| `Divider`        | `#1B2236`  | Separadores horizontais sutis             |

### 2.3 Texto

| Token            | Hex        | Uso                                       |
| ---------------- | ---------- | ----------------------------------------- |
| `Text`           | `#F1F5F9`  | Texto principal                           |
| `TextMuted`      | `#94A3B8`  | Labels, hints, texto secundário           |
| `TextSubtle`     | `#64748B`  | Placeholders, texto desabilitado          |
| `TextOnAccent`   | `#FFFFFF`  | Texto sobre botão primário                |

### 2.4 Marca (Accent — Violet)

| Token            | Hex        | Uso                                                          |
| ---------------- | ---------- | ------------------------------------------------------------ |
| `Accent`         | `#8B5CF6`  | Botões primários, foco, indicador de aba ativa, links        |
| `AccentHover`    | `#A78BFA`  | Hover de elementos accent                                    |
| `AccentSubtle`   | `#3B2F66`  | Background de selection/ativo (10–15% opacidade visual)      |
| `AccentGlow`     | `#8B5CF6`  | Borda em foco de input (com `BorderThickness=2`)             |

### 2.5 Semânticas

| Token            | Hex        | Uso                                       |
| ---------------- | ---------- | ----------------------------------------- |
| `Success`        | `#10B981`  | Ações positivas, status OK                |
| `Warning`        | `#F59E0B`  | Avisos não-bloqueantes                    |
| `Danger`         | `#EF4444`  | Erros, ações destrutivas                  |
| `Info`           | `#06B6D4`  | Informações neutras (cyan)                |

### 2.6 Console / log

| Token            | Hex        | Uso                                       |
| ---------------- | ---------- | ----------------------------------------- |
| `ConsoleBg`      | `#070A14`  | Background do log de execução             |
| `ConsoleText`    | `#CBD5E1`  | Texto do log                              |

---

## 3. Tipografia

- **Fonte**: Segoe UI Variable, fallback `Segoe UI`, `Inter`, `system-ui`.
- **Escala**:
  | Style       | Tamanho | Peso | Uso                          |
  | ----------- | ------- | ---- | ---------------------------- |
  | `Display`   | 22px    | 600  | Título da janela             |
  | `Subtitle`  | 14px    | 400  | Subtítulo / descrição        |
  | `Body`      | 13px    | 400  | Texto padrão                 |
  | `Label`     | 12px    | 500  | Labels de campos             |
  | `Hint`      | 11px    | 400  | Hints / mensagens auxiliares |
  | `Mono`      | 12px    | 400  | Cascadia Mono — logs, tokens |

---

## 4. Espaçamento e raio

- **Grid base**: 4px. Use múltiplos de 4 (`4, 8, 12, 16, 20, 24, 32`).
- **Padding de cards**: 20px.
- **Padding de inputs**: 12px horizontal, 10px vertical.
- **Raio**:
  - `Radius.Small` = 6px (inputs, botões pequenos)
  - `Radius.Medium` = 10px (botões padrão)
  - `Radius.Large` = 14px (cards, tab content)

---

## 5. Componentes

### 5.1 Button

Três variantes, sempre `Cursor=Hand`, transição de cor em 120ms.

| Variante                  | Bg                  | Fg               | Border           | Hover                                              |
| ------------------------- | ------------------- | ---------------- | ---------------- | -------------------------------------------------- |
| **Primary** (default)     | `Accent`            | `TextOnAccent`   | —                | bg → `AccentHover`                                 |
| **Secondary** (`Secondary`) | `SurfaceAlt`      | `Text`           | `Border`         | bg → `SurfaceHigh`, border → `BorderStrong`        |
| **Ghost** (`Ghost`)       | Transparent         | `TextMuted`      | —                | bg → `SurfaceAlt`, fg → `Text`                     |
| **Danger** (`Danger`)     | Transparent         | `Danger`         | `Danger` 40%     | bg → `Danger` 12%                                  |

Padding default: `16,10`. Height mínima: 36px. Border-radius: `Radius.Medium`.

### 5.2 Input (TextBox / PasswordBox)

- Background: `SurfaceAlt`
- Border: 1px `Border`, **2px `Accent`** quando focado
- Padding: `12,10`
- Raio: `Radius.Small`
- Caret: `Accent`
- Placeholder: `TextSubtle` italic

### 5.3 CheckBox

- Quadrado 18px, border 1px `BorderStrong`, radius 4px
- Checked: fill `Accent`, check branco
- Label à direita, gap 8px

### 5.4 TabControl

- Tabs header: sem background, bottom border 1px `Border`
- TabItem padding: `20,12`
- TabItem inativa: foreground `TextMuted`
- TabItem ativa: foreground `Text`, **bottom border 2px `Accent`**
- TabItem hover: foreground `Text`
- Conteúdo da tab: padding 24px, background `Surface`

### 5.5 Card

- Background: `Surface`
- Border: 1px `Border`
- Radius: `Radius.Large`
- Padding interno: 20–24px

### 5.6 DataGrid

- Background: transparent
- Header: bg `SurfaceAlt`, fg `TextMuted`, peso 500, padding 12,10
- Linha: bg `Surface`, alt `SurfaceAlt`
- Hover de linha: bg `SurfaceHigh`
- Border vertical: nenhuma. Horizontal: 1px `Divider`
- Sem foco visual de célula (read-only)

### 5.7 Log Console

- Background: `ConsoleBg`
- Border: 1px `Border`, radius `Radius.Large`
- Padding: 16px
- Fonte: `Mono` (12px)
- ScrollViewer: sempre auto

---

## 6. Estados

- **Foco**: borda 2px `Accent` em inputs; outline `Accent` em botões secondary/ghost.
- **Hover**: transição de 120ms via `ColorAnimation` ou via Trigger simples.
- **Disabled**: opacidade 0.45, cursor `Arrow`.
- **Loading**: botão fica disabled; o status textual ao lado mostra o passo atual.

---

## 7. Responsividade

A janela é redimensionável (min 900x600). Regras:

1. Todo card usa `Grid` com colunas `*` proporcionais — nada com `Width` fixo exceto botões.
2. `DataGrid` deve sempre estar dentro de um container com `*` height.
3. Textos longos: `TextWrapping=Wrap` em descrições, `TextTrimming=CharacterEllipsis` em paths.
4. Tabs nunca têm rolagem horizontal — em larguras pequenas, os títulos abreviam (ex: emoji + uma palavra).

---

## 8. Acessibilidade

- Contraste mínimo AA (4.5:1) entre `Text` (#F1F5F9) e `Bg` (#0B0F1A) → ratio ~16:1 ✓
- Contraste `TextMuted` (#94A3B8) sobre `Bg` → ratio ~7.2:1 ✓
- Botão primário `TextOnAccent` sobre `Accent` (#8B5CF6) → ratio ~5.1:1 ✓
- Todo `Button`, `CheckBox`, `TabItem` deve ter `Cursor=Hand`.
- `IsTabStop` mantido em todos os controles interativos.

---

## 9. Iconografia

- Usar emojis Unicode (🔐 📂 ⚙️ ▶️ 🚀 ✅ ❌ ⏭️ 💾 📅 🔍 🖼️ 📁) — não trazem dependência extra.
- Tamanho coerente com o texto (font-size do contexto).
- Nunca usar emoji como único veículo de significado — sempre acompanhado de texto.

---

## 10. Convenções XAML

1. Resources globais ficam **apenas** em `App.xaml`.
2. Estilos têm `x:Key` em PascalCase (`PrimaryButton`, `BodyText`).
3. Estilos default sem `x:Key` aplicáveis a `TextBox`, `Button`, `CheckBox`, `TabControl`, `TabItem` ficam em `App.xaml`.
4. Nenhuma janela define `Background` próprio — usa o token `Bg` herdado.
5. Nada de `<Window.Resources>` para tokens — apenas overrides locais raros (estados condicionais).
