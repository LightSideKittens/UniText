# Getting Started

This guide covers the basics of setting up and using UniText in your Unity project.

## 1. Adding UniText to a Scene

Use the **GameObject** menu to create ready-to-use UniText objects:

- **GameObject > UI > UniText - Text** — text with default font and size
- **GameObject > UI > UniText - Button** — button with UniText label (Image + Button + UniText child)
- **GameObject > UI > UniText - Input Field** — input field with text, placeholder, caret, and viewport

Canvas and EventSystem are created automatically if not present. Default font stack from **Project Settings > UniText** is applied to all created components.

You can also override default prefabs in **Project Settings > UniText** (Text Prefab, Button Prefab, Input Field Prefab) — the menu will instantiate your prefab instead.

```csharp
// Via code:
var uniText = gameObject.AddComponent<UniText>();
uniText.FontStack = myFontStack;
uniText.Text = "Hello, World!";
```

Note: Editor defaults (from Project Settings > UniText) are only applied when adding the component via the menu or Inspector.

---

## 2. Working with Fonts

UniText uses its own font format with two rendering modes:

| Mode | Description | Use Case |
|------|-------------|----------|
| **SDF** | Single-channel Signed Distance Field | Default. Resolution-independent, supports outlines and shadows |
| **MSDF** | Multi-channel Signed Distance Field | Sharper corners on geometric/display fonts |

Both modes use Burst-compiled curve-based rasterization (no bitmap rendering). Glyphs are stored in a shared `Texture2DArray` atlas with adaptive tile sizes (64/128/256), reference counting, and LRU eviction. Set the mode per component via `RenderMode`.

### 2.1 Creating a UniTextFont Asset

**Context Menu** (from fonts already in the project):

1. Import your font files (`.ttf`, `.otf`, or `.ttc`) into Unity
2. Select one or multiple fonts in the Project window
3. Right-click > **Create > UniText > Font Asset**
4. A `.asset` file is created next to each source font

Supports batch creation — select 10 fonts, get 10 assets in one click.

**UniText Tools Window** (also useful for creating from fonts outside the project):

If the font file is somewhere on your computer but not imported into the Unity project:

1. Open **Tools > UniText > Tools**
2. Drag-and-drop font files from the Project window, or click **Browse Files** to pick fonts from anywhere on your computer
3. Click **Create N UniText Font Asset(s)**
4. For external fonts, you will be prompted for an output folder within Assets

This is also useful for quick drag-and-drop workflow without manually importing fonts first.

Font bytes are embedded directly in the asset — there is no external file dependency at runtime.

### 2.2 Font Inspector Settings

Select a UniTextFont asset to configure in the Inspector:

| Setting | Default | Description |
|---------|---------|-------------|
| **Font Scale** | 1.0 | Visual scale multiplier. Normalizes fonts that appear too small or too large by design |
| **SDF Detail** | 1.0 | Tile detail multiplier. Higher values force larger atlas tiles for fonts with thin strokes (e.g. calligraphic) |
| **Glyph Overrides** | — | Per-glyph tile size overrides (Auto/64/128/256) for fine-tuning quality on specific glyphs |

After changing SDF Detail or Glyph Overrides, click **Apply** to rebuild the atlas. **Revert** discards pending changes.

A **Glyph Picker** is built into the inspector: type text to preview glyph rendering, select individual glyphs from the grid, and add tile size overrides directly.

The Inspector also shows:
- **Face Info** — family name, style, weight class, italic flag (read-only, extracted from font data)
- **Variable Font Axes** — if the font is variable, shows available axes with min/default/max values
- **Font Data Status** — whether font bytes are embedded
- **Runtime Data** — glyph count, character count
- **Atlas Preview** — SDF, MSDF, and Emoji atlas texture slices

### 2.3 Creating a UniTextFontStack (Font Collection)

UniTextFontStack organizes fonts into **Font Families**. Each family has a **primary** font and optional **faces** (bold, italic, light, etc.). Families are searched in order for glyph fallback.

There are two creation modes when you select multiple UniTextFont assets:

#### Font Stack (Combined) — Grouped by Family

1. Select 2+ **UniTextFont** assets in the Project window
2. Right-click > **Create > UniText > Font Stack (Combined)**
3. Fonts are automatically grouped by `familyName`. The closest-to-Regular font becomes the primary; others become faces.

```
Inter+Noto-Sans-Variable.asset
├── Family: Inter
│   ├── primary: Inter-Regular       (weight 400)
│   ├── face: Inter-Bold             (weight 700)
│   └── face: Inter-Italic           (weight 400, italic)
├── Family: NotoSansArabic
│   └── primary: NotoSansArabic-Regular
└── Family: NotoSansHebrew
    └── primary: NotoSansHebrew-Regular
```

When rendering "Hello مرحبا עולם":
- "Hello" — Inter family has Latin glyphs, used directly
- "مرحبا" — Inter has no Arabic glyphs, falls back to NotoSansArabic family
- "עולם" — Falls back to NotoSansHebrew family

When `<b>` is applied, the system uses CSS §5.2 weight matching to find the best face within the same family (e.g., Inter-Bold). If no matching face exists, synthesis (fake bold/italic) is applied.

**Use case:** Multilingual text with real bold/italic variants. One component handles any language.

#### Font Stack (Per Font) — Individual Stacks

1. Select 1+ **UniTextFont** assets in the Project window
2. Right-click > **Create > UniText > Font Stack (Per Font)**
3. Creates **one separate** UniTextFontStack for each selected font

**Use case:** When different components use different fonts. Swap font stacks per component.

#### Variable Fonts

Variable fonts are strongly recommended over static font files. A single variable font file replaces dozens of static weights/widths:

```
Inter-Variable.asset                    <- one file
├── wght axis: 100–900 (weight)
├── wdth axis: 75–100 (width)
└── replaces: Inter-Thin, Inter-Light, Inter-Regular, Inter-Medium,
              Inter-SemiBold, Inter-Bold, Inter-ExtraBold, Inter-Black
```

Variable font axes are controlled via modifiers. `<b>` and `<i>` automatically set the appropriate axes when the font supports them. For direct control, use the VariationModifier with `<var>` tags.

#### Three-Tier Face Resolution

When a modifier requests bold or italic, the system resolves in order:
1. **Variable font axes** — if the font has `wght`/`ital`/`slnt` axes, set them directly
2. **Static font faces** — find the closest matching face by weight/italic in the family
3. **Synthesis** — apply fake bold (SDF dilate) or fake italic (shear transform)

#### Fallback Stack Chaining

UniTextFontStack has a `fallbackStack` field that references another UniTextFontStack. The system searches primary fonts in each family first, then walks the `fallbackStack` chain. Circular references are handled automatically.

```
LanguageSupportStack                    <- create once
├── Family: NotoSansArabic
├── Family: NotoSansHebrew
├── Family: NotoSansDevanagari
└── Family: NotoSansCJK

HeadingStack                            <- for headings
├── Family: Montserrat (primary + bold/italic faces)
└── fallbackStack → LanguageSupportStack

BodyStack                               <- for body text
├── Family: Inter (primary + faces)
└── fallbackStack → LanguageSupportStack
```

All stacks get full language support through one shared reference.

### 2.4 Material Management

Materials are managed automatically by `UniTextMaterialCache`. There is no manual material assignment — the system creates and caches shared materials for SDF Face, SDF Base, MSDF Face, and MSDF Base internally.

Effects like outlines and shadows use multi-pass rendering: the effect layer renders first (Base material), then the text face renders on top (Face material). This is handled automatically when you use `<outline>` or `<shadow>` tags.

### 2.5 UniText Tools Window

Open via **Tools > UniText Tools**. Three tabs:

#### Tab 1: Create Font Asset

Batch creation of UniTextFont assets from source files.

**Adding fonts:**
- **Drag & drop** — drop `.ttf`/`.otf`/`.ttc` files into the drop area
- **Browse Files** — opens file dialog with multi-select
- **Project selection** — selecting font files in the Project window auto-adds them

Each entry shows the font name and file size. Click **Create N UniText Font Asset(s)** to generate all assets.

**Additional features:**
- **Copy All Characters** — extracts every codepoint the font supports and copies to clipboard. Useful for checking font coverage or as input for the Font Subsetter

**Output:**
- Project fonts (within Assets): saved next to the source file
- External fonts (outside Assets): prompts for output folder

#### Tab 2: Font Subsetter

Create optimized subset fonts by keeping or removing specific character ranges. Reduces font file size for builds where you don't need full Unicode coverage.

**Two modes:**

**Keep Mode** — only selected characters remain in the font:
- Select script ranges (Latin, Cyrillic, Arabic, etc.) and/or type custom text
- The output font contains only those characters (plus GSUB-related composed forms)
- Example: Keep only "Basic Latin + Cyrillic" for a game targeting English/Russian

**Remove Mode** — selected characters are removed from the font:
- Select script ranges and/or type custom text to remove
- Intelligent composition detection: combined characters (emoji sequences, ligatures) are removed as glyphs while preserving their component codepoints
- Two-pass process:
  1. Codepoint removal with GSUB closure (handles contextual forms)
  2. Composition glyph removal without closure (preserves components)
- Example: Remove CJK range from a font that covers everything

**Available script ranges (30 sets in 10 groups):**

| Group | Ranges |
|-------|--------|
| Latin | Basic Latin, Extended Latin, Vietnamese |
| European | Cyrillic, Greek, Armenian, Georgian |
| Semitic | Arabic, Hebrew |
| N. Indic | Devanagari, Bengali, Gujarati, Gurmukhi |
| S. Indic | Tamil, Telugu, Kannada, Malayalam |
| SE Asian | Thai, Lao, Myanmar, Khmer |
| E. Asian | Hiragana, Katakana |
| Other | Sinhala, Tibetan |
| Symbols (1) | Digits, Punctuation, Currency, Math |
| Symbols (2) | Arrows, Box Drawing |

**Output:** Saves a new `.ttf` file with the suffix `_subset`. Reports original size, subset size, and reduction percentage.

**Practical scenarios:**

| Scenario | Mode | Configuration |
|----------|------|---------------|
| Mobile game, English only | Keep | Basic Latin + Digits + Punctuation |
| European app, no Asian scripts | Remove | Devanagari, Bengali, Tamil, Thai, CJK, etc. |
| Localized to Arabic + English | Keep | Basic Latin + Arabic + Digits + Punctuation |
| Remove unused emoji from Noto | Remove | Custom text with emoji codepoints |

#### Tab 3: Dictionary Builder

Builds word segmentation dictionary assets for SE Asian scripts (Thai, Lao, Khmer, Myanmar) that don't use spaces between words.

1. Drag-and-drop a word list text file (one word per line)
2. Select the target script
3. Click **Build** to compile a `WordSegmentationDictionary` asset

The compiled dictionary is configured via **Project Settings > UniText > Word Segmentation > Dictionaries**. UniText ships with a Thai dictionary (26K words from ICU).

---

## 3. Markup System

UniText features an extensible markup system based on **Modifiers** and **Parse Rules**.

### 3.1 Architecture: Rule + Modifier

The system separates **what to parse** from **what to do**:

- **Parse Rule** (`IParseRule`) — finds patterns in text and produces ranges with optional parameters
- **Modifier** (`BaseModifier`) — applies a visual or structural effect to those ranges

There is **no hard coupling** between tags and modifiers. Any parse rule can drive any modifier. The tag name, the syntax, and even the parsing strategy are all independent from the effect being applied. A `<highlight>` tag can trigger a ColorModifier. A `**markdown**` wrapper can trigger an OutlineModifier. You decide.

**Example**: The same BoldModifier works with completely different syntaxes:

| Parse Rule | Syntax | Modifier |
|------------|--------|----------|
| TagRule (tagName="b") | `<b>bold</b>` | BoldModifier |
| TagRule (tagName="strong") | `<strong>bold</strong>` | BoldModifier |
| MarkdownWrapRule (marker="**") | `**bold**` | BoldModifier |
| RangeRule (range="..") | *(entire text, no markup)* | BoldModifier |

And the same TagRule (tagName="b") can be paired with any modifier — BoldModifier, ColorModifier, or your own custom modifier.

### 3.2 Built-in Modifiers

The table below shows **default pairings** (how presets configure them). These are conventions, not constraints — you can reassign any tag to any modifier.

| Default Tag | Modifier | Example |
|-------------|----------|---------|
| `<b>` | BoldModifier | `<b>bold</b>` or `<b=700>weight 700</b>` |
| `<i>` | ItalicModifier | `<i>italic</i>` |
| `<u>` | UnderlineModifier | `<u>underline</u>` |
| `<s>` | StrikethroughModifier | `<s>strike</s>` |
| `<color>` | ColorModifier | `<color=#FF0000>red</color>` |
| `<size>` | SizeModifier | `<size=24>large</size>` |
| `<gradient>` | GradientModifier | `<gradient=rainbow>text</gradient>` |
| `<cspace>` | LetterSpacingModifier | `<cspace=5>wider</cspace>` |
| `<line-height>` | LineHeightModifier | `<line-height=1.5>text</line-height>` |
| `<line-spacing>` | LineHeightModifier | `<line-spacing=10>text</line-spacing>` |
| `<upper>` | UppercaseModifier | `<upper>text</upper>` |
| `<ellipsis>` | EllipsisModifier | `<ellipsis=1>long text</ellipsis>` |
| `<li>` | ListModifier | `<li>bullet item</li>` |
| `<link>` | LinkModifier | `<link=url>click</link>` |
| `<obj>` | ObjModifier | `<obj=icon/>` |
| `<outline>` | OutlineModifier | `<outline=#000>text</outline>` or `<outline=0.3,#FF0000>` |
| `<shadow>` | ShadowModifier | `<shadow=#00000080>text</shadow>` or `<shadow=0.1,#000,2,2,0.5>` |
| `<var>` | VariationModifier | `<var=700>weight</var>` (direct axis control) |

### 3.3 Custom Tags with Default Parameters

TagRule has a `defaultParameter` field that lets you create custom tags with pre-configured values. This way your text stays clean — no need to repeat parameter values in every tag.

**Example**: Create a `<warning>` tag that always applies red color:

```
Style:
  Rule: TagRule (tagName = "warning", defaultParameter = "#FF0000")
  Modifier: ColorModifier
```

Now in text:
- `<warning>error occurred</warning>` — uses default red (#FF0000)
- `<warning=#FFA500>caution</warning>` — overrides with orange

**Multi-parameter defaults**: For modifiers with multiple parameters (like OutlineModifier: dilate, color), defaults fill in missing values:

```
Style:
  Rule: TagRule (tagName = "glow", defaultParameter = "0.3,#00FF00")
  Modifier: OutlineModifier
```

- `<glow>text</glow>` — dilate 0.3, green outline
- `<glow=0.5>text</glow>` — dilate 0.5, green outline (color from default)

This works because TagRule merges text parameters with defaults: values from the tag take priority, remaining parameters come from `defaultParameter`.

MarkdownWrapRule also supports `defaultParameter` the same way.

### 3.4 Parse Rule Types

#### Tag-Based Rules

All tag-based rules use the universal **TagRule** class with a configurable tag name. Parameters are always optional. Self-closing is syntax-driven (`<tag/>` or `<tag=value/>`).

#### Markdown-Style Rules

| Parse Rule | Syntax | Typical Modifier |
|------------|--------|----------|
| MarkdownWrapRule (`**`) | `**bold**` | BoldModifier |
| MarkdownWrapRule (`*`) | `*italic*` | ItalicModifier |
| MarkdownWrapRule (`~~`) | `~~strike~~` | StrikethroughModifier |
| MarkdownLinkParseRule | `[text](url)` | LinkModifier |
| MarkdownListParseRule | `- item`, `* item`, `1. item` | ListModifier |
| RawUrlParseRule | Auto-detects `https://...` URLs | LinkModifier |

#### Utility Rules

| Parse Rule | Purpose |
|------------|---------|
| RangeRule | Apply modifier to specific character ranges without any markup in text |
| StringParseRule | Match and optionally replace literal string patterns |
| CompositeParseRule | Groups multiple rules under one modifier — each position in text is checked against child rules in order until one matches |

### 3.5 Parameter Formats Reference

**Color:**
- Hex: `#RGB`, `#RRGGBB`, `#RRGGBBAA`
- Named (20 colors): white, black, red, green, blue, yellow, cyan, magenta, orange, purple, gray, lime, brown, pink, navy, teal, olive, maroon, silver, gold

**Size:**
- Absolute: `<size=24>` — 24 pixels
- Percentage: `<size=150%>` — 150% of base size
- Relative: `<size=+10>` / `<size=-5>` — offset from base

**Gradient:**
- Format: `<gradient=name[,shape][,angle]>`
- Shapes: `linear` (default), `radial`, `angular`
- Angle: 0–360 degrees (0=right, 90=up). Used by `linear` and `angular`
- Examples:
  - `<gradient=rainbow>` — linear, horizontal
  - `<gradient=rainbow,radial>` — radial from center
  - `<gradient=rainbow,angular,90>` — conic sweep, rotated 90°
  - `<gradient=rainbow,linear,45>` — linear, rotated 45°

Gradients are defined in the **UniTextGradients** asset (Project Settings > UniText > Gradients).

**Letter spacing:**
- Format: `<cspace=spacing[,monospace]>`
- Pixels: `<cspace=5>` — 5px extra spacing
- Em units: `<cspace=0.1em>` — 0.1 em extra spacing
- Monospace: `<cspace=0.5em,true>` — equal advance width for all glyphs
- For cursive scripts (Arabic, Syriac, etc.), positive spacing renders visual tatweel (kashida) to preserve connections

**Outline:**
- `<outline>` — default (dilate=0.2, black)
- `<outline=0.3>` — custom dilate
- `<outline=#FF0000>` — custom color
- `<outline=0.3,#FF0000>` — both

**Shadow:**
- `<shadow>` — default (black 50% alpha)
- `<shadow=#00000080>` — custom color
- `<shadow=0.1,#000,2,2,0.5>` — dilate, color, offsetX, offsetY, softness

**Variable font axes (`<var>`):**
- Positional axis values in order: wght, wdth, ital, slnt, opsz
- Use `~` to skip an axis
- Absolute: `<var=700>` — weight 700
- Percentage: `<var=150%>` — 150% of default weight
- Delta: `<var=+200>` — +200 from default weight
- Multiple axes: `<var=700,80>` — weight 700, width 80
- Skip axes: `<var=~,~,~,-12>` — only set slant to -12

**Ellipsis (text truncation):**
- `<ellipsis=1>` — truncate end (default): `Hello Wo...`
- `<ellipsis=0>` — truncate start: `...o World`
- `<ellipsis=0.5>` — truncate middle: `Hel...rld`
- Any float 0-1 for fine-grained control

### 3.6 Adding Styles to a Component

#### In the Inspector

1. Expand **Styles** list on the UniText component
2. Click **+** — a searchable selector opens with ~30 predefined presets (Bold, Italic, Outline, Shadow, Markdown variants, etc.)
3. Select a preset — both the Rule and Modifier are configured automatically

Each entry is a Rule+Modifier pair. Tags from the Rule are parsed in text, and the Modifier applies the effect to matched ranges. You can also configure Rule and Modifier manually for custom combinations.

#### Via Code

```csharp
uniText.AddStyle(new Style
{
    Rule = new TagRule { tagName = "color" },
    Modifier = new ColorModifier()
});
```

Remove at runtime:

```csharp
bool removed = uniText.RemoveStyle(style);

// Or remove all:
uniText.ClearStyles();
```

### 3.7 Style Preset — Shared Configuration

**Problem:** You have 50 UniText components that all need the same set of modifiers (bold, italic, color, links). Setting up each one manually is tedious and error-prone.

**Solution:** Style Preset is a ScriptableObject that stores a reusable list of Rule+Modifier pairs.

#### Setup

1. **Assets > Create > UniText > Style Preset**
2. Add your modifier pairs:

```
MyModConfig.asset
├── [0] BoldModifier + TagRule (b)
├── [1] ItalicModifier + TagRule (i)
├── [2] ColorModifier + TagRule (color)
├── [3] LinkModifier + TagRule (link)
└── [4] UnderlineModifier + TagRule (u)
```

3. On each UniText component, add this config to the **Style Presets** list

#### Benefits

- **Single source of truth** — change the config, all components update
- **No duplication** — define modifiers once, reference everywhere
- **Combinable** — a component can have multiple configs plus its own local Styles. They all work together
- **Version control friendly** — one asset to track rather than per-component settings

#### Local vs Config

| Feature | Local Styles | Style Presets |
|---------|-------------------|-------------------|
| Scope | Per-component | Shared across components |
| Edit location | UniText Inspector | Preset asset Inspector |
| Use case | Component-specific markup | Project-wide standard markup |

A component's effective set of modifiers = its local Styles + all Style Presets.

### 3.8 RangeRule — Applying Modifiers Without Markup

RangeRule lets you apply a modifier to specific text ranges **programmatically**, without any tags in the text itself.

#### Use Case: Apply to All Text

To apply a modifier to the entire text (e.g., make everything a specific color), use the range `".."`:

```csharp
var rangeRule = new RangeRule();
rangeRule.data.Add(new RangeRule.Data
{
    range = "..",           // ".." means the full text range
    parameter = "#FF0000"  // parameter passed to the modifier
});

uniText.AddStyle(new Style
{
    Rule = rangeRule,
    Modifier = new ColorModifier()  // entire text becomes red
});
```

#### Range Syntax

RangeRule uses C#-style range notation:

| Range | Meaning |
|-------|---------|
| `".."` | Entire text (start to end) |
| `"0..10"` | Codepoints 0 through 9 |
| `"5.."` | From codepoint 5 to end |
| `"..5"` | From start to codepoint 4 |
| `"2..^3"` | From codepoint 2 to 3 from end |
| `"^5.."` | Last 5 codepoints |

#### Multiple Ranges

```csharp
var rangeRule = new RangeRule();
rangeRule.data.Add(new RangeRule.Data { range = "0..5", parameter = "#FF0000" });
rangeRule.data.Add(new RangeRule.Data { range = "10..20", parameter = "#00FF00" });

uniText.AddStyle(new Style
{
    Rule = rangeRule,
    Modifier = new ColorModifier()
});
// Codepoints 0-4 are red, 10-19 are green
```

#### Practical Scenarios

| Scenario | Range | Modifier |
|----------|-------|----------|
| Bold the entire text | `".."` | BoldModifier |
| Highlight first word (5 chars) | `"0..5"` | ColorModifier with color parameter |
| Underline last 10 chars | `"^10.."` | UnderlineModifier |
| Apply size to specific range | `"3..8"` | SizeModifier with size parameter |

### 3.9 StringParseRule — Literal Pattern Matching

StringParseRule matches literal string patterns in text (no XML/HTML syntax):

```csharp
var emojiRule = new StringParseRule();
emojiRule.patterns = new[] { ":)", ":(", ":D" };
emojiRule.hasReplacement = true;
emojiRule.replacement = "😊";

uniText.AddStyle(new Style
{
    Rule = emojiRule,
    Modifier = new EmptyModifier()  // no visual effect, just replacement
});
// ":)" in text gets replaced with "😊"
```

### 3.10 CompositeParseRule — Combining Rules

CompositeParseRule groups multiple rules into one. It tries child rules in order and returns the first match:

```csharp
var composite = new CompositeParseRule();
composite.rules.Add(new TagRule { tagName = "link" }); // <link=url>text</link>
composite.rules.Add(new MarkdownLinkParseRule()); // [text](url)
composite.rules.Add(new RawUrlParseRule());       // auto-detect https://...

uniText.AddStyle(new Style
{
    Rule = composite,
    Modifier = new LinkModifier()
});
// All three link syntaxes work with a single modifier
```

### 3.11 Priority System

Parse rules have a `Priority` property that controls matching order (higher = matched first):

| Priority | Use Case | Example |
|----------|----------|---------|
| Positive (e.g., 10) | Explicit markup should match before anything else | Custom rules |
| 0 (default) | Standard tag-based and markdown rules | TagRule, MarkdownWrapRule, MarkdownLinkParseRule |
| Negative (e.g., -100) | Auto-detection, should only match if nothing else did | RawUrlParseRule (-100) |

This prevents conflicts: `<link=url>https://example.com</link>` won't be double-matched by both TagRule and RawUrlParseRule.

### 3.12 Creating Custom Parse Rules

Implement `IParseRule` to create your own markup syntax:

```csharp
public interface IParseRule
{
    int Priority => 0;
    int TryMatch(string text, int index, PooledList<ParsedRange> results);
    void Finalize(string text, PooledList<ParsedRange> results) { }
    void Reset() { }
}
```

**Simplest approach — use TagRule:**

If your syntax follows the `<tag>content</tag>` pattern, use the built-in `TagRule` with a custom tag name — no subclassing needed:

```csharp
// In Inspector: add a TagRule, set tagName = "highlight"
// Now <highlight=yellow>text</highlight> works automatically
```

Parameters are always optional. Self-closing is purely syntax-driven (`<tag/>` or `<tag=value/>`).

### 3.13 Creating Custom Modifiers

UniText has three modifier base classes for different use cases:

#### Pattern 1: Text Transformation (BaseModifier)

For modifiers that transform codepoints before rendering (like uppercase):

```csharp
[Serializable]
public class LowercaseModifier : BaseModifier
{
    protected override void OnEnable() { }
    protected override void OnDisable() { }
    protected override void OnDestroy() { }

    protected override void OnApply(int start, int end, string parameter)
    {
        var codepoints = buffers.codepoints.data;
        var count = buffers.codepoints.count;
        var clampedEnd = Math.Min(end, count);

        for (var i = start; i < clampedEnd; i++)
            codepoints[i] = char.ToLowerInvariant((char)codepoints[i]);
    }
}
```

#### Pattern 2: Per-Glyph Visual Effect (GlyphModifier\<T\>)

For modifiers that change glyph appearance during mesh generation (color, underline, etc.):

```csharp
[Serializable]
public class HighlightModifier : GlyphModifier<byte>
{
    [SerializeField] private Color highlightColor = Color.yellow;

    protected override string AttributeKey => "highlight";

    protected override Action GetOnGlyphCallback() => OnGlyph;

    protected override void DoApply(int start, int end, string parameter)
    {
        var buffer = attribute.buffer.data;
        buffer.SetFlagRange(start, Math.Min(end, buffers.codepoints.count));
    }

    private void OnGlyph()
    {
        var gen = UniTextMeshGenerator.Current;
        if (!attribute.buffer.data.HasFlag(gen.currentCluster))
            return;

        var colors = gen.Colors;
        var baseIdx = gen.vertexCount - 4;
        colors[baseIdx] = colors[baseIdx + 1] =
        colors[baseIdx + 2] = colors[baseIdx + 3] = highlightColor;
    }
}
```

#### Pattern 3: Interactive Region (InteractiveModifier)

For clickable/hoverable text regions:

```csharp
[Serializable]
public class HashtagModifier : InteractiveModifier
{
    public override string RangeType => "hashtag";
    public override int Priority => 50;

    public event Action<string> HashtagClicked;

    protected override void OnApply(int start, int end, string parameter)
    {
        AddRange(start, end, parameter); // Register clickable region
    }

    protected override void HandleRangeClicked(InteractiveRange range, TextHitResult hit)
    {
        HashtagClicked?.Invoke(range.data);
    }

    protected override void HandleRangeEntered(InteractiveRange range, TextHitResult hit) { }
    protected override void HandleRangeExited(InteractiveRange range) { }
}
```

#### Modifier Lifecycle

```
SetOwner(uniText)           <- attached to component
    |
Prepare()                   <- lazy init on first Apply (allocate buffers)
    |
PrepareForParallel()        <- cache main-thread-only values before worker threads
    |
Apply(start, end, param)    <- called per matched range (calls OnApply)
    |
OnDisable()                 <- text changed, unsubscribe from events
    |
OnDestroy()                 <- component destroyed, release all resources
```

#### Best Practices for Custom Modifiers

- **No `new T[]` at runtime** — use `UniTextArrayPool<T>.Rent/Return` or `buffers.GetOrCreateAttributeData<T>()`
- **Subscribe in OnEnable, unsubscribe in OnDisable** — prevents stale callbacks
- **Use `PrepareForParallel()`** for anything that calls Unity API (`Material.GetFloat()`, etc.)
- **Modifiers are fully encapsulated** — external code doesn't need to know about them. If a modifier adds geometry, it calls UniTextMeshGenerator methods internally

---

## 4. Interactive Text

UniText provides built-in support for clickable regions, hover detection, and visual feedback.

### Click and Hover Events

```csharp
// Any text click
uniText.TextClicked += hit => Debug.Log($"Clicked cluster: {hit.cluster}");

// Interactive range events (links, custom ranges)
uniText.RangeClicked += hit => Debug.Log($"Clicked: {hit.range.data}");
uniText.RangeEntered += hit => Debug.Log($"Hover enter: {hit.range.data}");
uniText.RangeExited += hit => Debug.Log($"Hover exit: {hit.range.data}");

// Continuous hover tracking
uniText.HoverChanged += hit => Debug.Log($"Hover at cluster: {hit.cluster}");
```

### Hit Testing

For custom interaction logic:

```csharp
// Local space
TextHitResult hit = uniText.HitTest(localPosition);

// Screen space
TextHitResult hit = uniText.HitTestScreen(screenPosition, eventCamera);

// Get visual bounds for a cluster range
var bounds = new List<Rect>();
uniText.GetRangeBounds(startCluster, endCluster, bounds);
```

### Text Highlighter

The `Highlighter` property controls visual feedback. The built-in `DefaultTextHighlighter` provides click and hover animations:

```csharp
if (uniText.Highlighter is DefaultTextHighlighter highlighter)
{
    highlighter.ClickColor = new Color(1, 0, 0, 0.5f);
    highlighter.HoverColor = new Color(0, 0, 1, 0.1f);
    highlighter.FadeDuration = 0.5f;
}

// Disable highlighting
uniText.Highlighter = null;
```

Implement your own by extending `TextHighlighter` and overriding `OnRangeClicked`, `OnRangeEntered`, `OnRangeExited`, `Update`.

---

## 5. RTL and Bidirectional Text

UniText automatically handles:
- **RTL scripts** (Arabic, Hebrew) — text flows right-to-left
- **BiDi mixing** — "Hello עולם World" renders correctly
- **Complex shaping** — Arabic ligatures, Indic conjuncts, etc. (via HarfBuzz)

### Direction Settings

- **Auto** (default) — detects from first strong directional character
- **LeftToRight** — force left-to-right
- **RightToLeft** — force right-to-left

```csharp
uniText.BaseDirection = TextDirection.Auto;
uniText.Text = "مرحبا بالعالم"; // Renders right-to-left
```

---

## 6. Emoji

Emoji work automatically — the system emoji font is detected and used:

```csharp
uniText.Text = "Hello! 👋 Great job! 🎉";
```

| Platform | Emoji Font |
|----------|------------|
| Windows | Segoe UI Emoji |
| macOS | Apple Color Emoji |
| iOS | Core Text (native API) |
| Android | NotoColorEmoji (via fonts.xml) |
| Linux | NotoColorEmoji / Symbola |
| WebGL | Browser Canvas 2D |

Emoji are rendered as color bitmaps in a separate atlas. The emoji font is checked first for emoji-presentation codepoints, then falls back to the regular font stack.

---

## 7. Common Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Text` | string | `""` | Text content with optional markup |
| `FontStack` | UniTextFontStack | — | Font collection with font families and fallback chain |
| `RenderMode` | RenderMode | SDF | SDF (single-channel) or MSDF (multi-channel) |
| `FontSize` | float | 36 | Base font size in points |
| `color` | Color | white | Base text color |
| `BaseDirection` | TextDirection | Auto | LTR, RTL, or Auto |
| `WordWrap` | bool | true | Enable/disable word wrapping |
| `HorizontalAlignment` | HorizontalAlignment | Left | Left, Center, Right |
| `VerticalAlignment` | VerticalAlignment | Top | Top, Middle, Bottom |
| `AutoSize` | bool | false | Auto-fit text to container |
| `MinFontSize` | float | 10 | Auto-size minimum |
| `MaxFontSize` | float | 72 | Auto-size maximum |
| `Highlighter` | TextHighlighter | DefaultTextHighlighter | Interaction visual feedback |

### Read-Only Properties

| Property | Type | Description |
|----------|------|-------------|
| `CleanText` | string | Text with all markup stripped |
| `CurrentFontSize` | float | Effective font size (after auto-sizing) |
| `ResultSize` | Vector2 | Computed text dimensions |
| `ResultGlyphs` | ReadOnlySpan\<PositionedGlyph\> | All positioned glyphs after layout |

### Events

| Event | Type | Description |
|-------|------|-------------|
| `TextClicked` | Action\<TextHitResult\> | Any text click |
| `RangeClicked` | Action\<InteractiveRangeHit\> | Interactive range clicked |
| `RangeEntered` | Action\<InteractiveRangeHit\> | Pointer enters interactive range |
| `RangeExited` | Action\<InteractiveRangeHit\> | Pointer exits interactive range |
| `HoverChanged` | Action\<TextHitResult\> | Pointer moved over text |
| `Rebuilding` | Action | Before text rebuild |
| `RectHeightChanged` | Action | RectTransform height changed |

---

## 8. Code Examples

### Basic Usage

```csharp
public class Example : MonoBehaviour
{
    [SerializeField] private UniText uniText;

    void Start()
    {
        uniText.Text = "Hello, World!";
        uniText.FontSize = 24;
        uniText.HorizontalAlignment = HorizontalAlignment.Center;
    }
}
```

### Clickable Links

```csharp
private LinkModifier linkModifier;

void Start()
{
    linkModifier = new LinkModifier();
    linkModifier.AutoOpenUrl = false;
    uniText.AddStyle(new Style
    {
        Modifier = linkModifier,
        Rule = new TagRule { tagName = "link" }
    });

    uniText.Text = "Visit <link=https://example.com>our website</link> for more info.";

    linkModifier.LinkClicked += url => Application.OpenURL(url);
    linkModifier.LinkEntered += url => Debug.Log($"Hovering: {url}");
    linkModifier.LinkExited += () => Debug.Log("Left link");
}
```

### Markdown Links and Auto-URL Detection

```csharp
// Markdown-style links
uniText.AddStyle(new Style
{
    Modifier = new LinkModifier(),
    Rule = new MarkdownLinkParseRule()
});
uniText.Text = "Visit [our website](https://example.com) for details.";

// Auto-detect raw URLs
uniText.AddStyle(new Style
{
    Modifier = new LinkModifier(),
    Rule = new RawUrlParseRule()
});
uniText.Text = "Check https://example.com for updates.";
```

### Inline Objects (Icons in Text)

```csharp
// Requires: ObjModifier + ObjParseRule registered
// ObjModifier must have InlineObject named "coin" with RectTransform prefab

uniText.Text = "You earned <obj=coin/> 100 gold!";
```

### Lists

```csharp
// With MarkdownListParseRule + ListModifier registered:
uniText.Text = "Shopping list:\n- Apples\n- Bananas\n- Oranges";

// Ordered list:
uniText.Text = "Steps:\n1. Open app\n2. Click button\n3. Done";
```

### Apply Color to Entire Text (RangeRule)

```csharp
var rangeRule = new RangeRule();
rangeRule.data.Add(new RangeRule.Data { range = "..", parameter = "#FF6600" });

uniText.AddStyle(new Style
{
    Rule = rangeRule,
    Modifier = new ColorModifier()
});

uniText.Text = "This entire text is orange.";
```

### Emoji

```csharp
uniText.Text = "Hello! 👋 Great job! 🎉";
```