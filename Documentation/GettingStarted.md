# Getting Started

This guide covers the basics of setting up and using UniText in your Unity project.

## 1. Adding UniText to a Scene

Add the UniText component to any UI GameObject:

1. Select any GameObject with **RectTransform** (or create via **GameObject > UI > Image**)
2. Add component: **Add Component > UniText**
3. Enter text in the **Text** field

Default fonts and appearance from Project Settings are applied automatically.

```csharp
// Via code — you must assign FontStack and Appearance manually:
var uniText = gameObject.AddComponent<UniText>();
uniText.FontStack = myFontStack;       // Required
uniText.Appearance = myAppearance;     // Required
uniText.Text = "Hello, World!";
```

Note: Editor defaults (from Project Settings > UniText) are only applied when adding the component via Inspector.

---

## 2. Working with Fonts

UniText uses its own font format supporting three rendering modes:

| Mode | Description | Use Case |
|------|-------------|----------|
| **SDF** | Signed Distance Field — resolution-independent | Default. Supports outlines, shadows, glow at any scale |
| **Smooth** | Anti-aliased grayscale bitmap | Pixel-perfect at specific size |
| **Mono** | 1-bit monochrome bitmap | Retro/pixel art style |

### 2.1 Creating a UniTextFont Asset

**Context Menu** (from fonts already in the project):

1. Import your font files (`.ttf`, `.otf`, or `.ttc`) into Unity
2. Select one or multiple fonts in the Project window
3. Right-click > **Create > UniText > Font Asset**
4. A `.asset` file is created next to each source font

Supports batch creation — select 10 fonts, get 10 assets in one click.

**Font Tools Window** (also useful for creating from fonts outside the project):

If the font file is somewhere on your computer but not imported into the Unity project:

1. Open **Tools > UniText > Font Tools**
2. Drag-and-drop font files from the Project window, or click **Browse Files** to pick fonts from anywhere on your computer
3. Click **Create N UniText Font Asset(s)**
4. For external fonts, you will be prompted for an output folder within Assets

This is also useful for quick drag-and-drop workflow without manually importing fonts first.

Font bytes are embedded directly in the asset — there is no external file dependency at runtime.

### 2.2 Font Inspector Settings

Select a UniTextFont asset to configure in the Inspector:

| Setting | Range | Default | Description |
|---------|-------|---------|-------------|
| **Sampling Point Size** | 8–256 | 90 | Size at which glyphs are rasterized into the atlas. Higher = more detail but larger atlas |
| **Atlas Size** | 256–4096 | 1024 | Atlas texture dimensions (square). Increase if many glyphs need to fit |
| **Spread Strength** | 0.1–1.0 | 0.25 | SDF spread as fraction of point size. Controls how far the distance field extends. Higher = smoother edges at large scales but thicker padding |
| **Render Mode** | SDF/Smooth/Mono | SDF | Glyph rendering mode |

After changing settings, click **Apply** to rebuild the atlas.

The Inspector also shows:
- **Font Data Status** — whether font bytes are embedded
- **Runtime Data** — glyph count, character count, atlas memory
- **Atlas Preview** — visual preview of atlas texture(s)

### 2.3 Creating a UniTextFontStack (Font Collection)

UniTextFontStack defines which fonts to use and in what fallback order. The first font is the main font; subsequent fonts serve as fallbacks for glyphs the main font doesn't have.

There are two creation modes when you select multiple UniTextFont assets:

#### Fonts (Combined) — Single Stack with Fallback Chain

1. Select 2+ **UniTextFont** assets in the Project window
2. Right-click > **Create > UniText > Fonts (Combined)**
3. Creates **one** UniTextFontStack containing all selected fonts

```
UniTextFontStack (Combined)
├── Inter-Regular        <- fonts[0] — main font (Latin, Cyrillic)
├── NotoSansArabic       <- fonts[1] — fallback for Arabic
└── NotoSansHebrew       <- fonts[2] — fallback for Hebrew
```

When rendering "Hello مرحبا עולם":
- "Hello" — Inter-Regular has Latin glyphs, used directly
- "مرحبا" — Inter-Regular has no Arabic glyphs, falls back to NotoSansArabic
- "עולם" — Neither of the above has Hebrew, falls back to NotoSansHebrew

**Use case:** Multilingual text in a single component. One component can render text in any language.

#### Fonts (Per Font) — Individual Stacks

1. Select 1+ **UniTextFont** assets in the Project window
2. Right-click > **Create > UniText > Fonts (Per Font)**
3. Creates **one separate** UniTextFontStack for each selected font

```
Inter-Regular FontStack.asset     <- contains only Inter-Regular
NotoSansArabic FontStack.asset    <- contains only NotoSansArabic
NotoSansHebrew FontStack.asset    <- contains only NotoSansHebrew
```

**Use case:** When different components use different fonts. Swap font stacks per component rather than building fallback chains.

#### Fallback Stack Chaining

UniTextFontStack has a `fallbackStack` field that references another UniTextFontStack. The system searches the primary fonts first, then walks the `fallbackStack` chain. Circular references are handled automatically.

This is useful when you have a shared set of language fonts that you want to reuse across different stacks without copying the same font list everywhere.

**Example:** Create one stack with all language fallbacks, then reference it from any other stack:

```
LanguageSupportStack                    <- create once
├── NotoSansArabic
├── NotoSansHebrew
├── NotoSansDevanagari
├── NotoSansThai
└── NotoSansCJK

HeadingStack                            <- for headings
├── Montserrat-Bold
└── fallbackStack → LanguageSupportStack

BodyStack                               <- for body text
├── Inter-Regular
└── fallbackStack → LanguageSupportStack

CaptionStack                            <- for captions
├── Inter-Light
└── fallbackStack → LanguageSupportStack
```

All three stacks get full language support through one shared reference. Add a new language font to `LanguageSupportStack` — all stacks pick it up automatically.

### 2.4 Understanding UniTextAppearance

**UniTextAppearance** maps fonts to rendering materials. It separates *what fonts to use* from *how to render them*.

#### Why Separate Appearance?

Different fonts in a fallback chain may need different materials:

- **Connected scripts (Arabic, Persian, Urdu)** — letters connect into words. A single-pass outline/shadow creates visible seams at glyph boundaries. Solution: 2-pass shader (outline layer first, then text on top)
- **Different SDF spread** — fonts generated with different spread values need matching shader settings
- **Visual variety** — some fonts need glow, others need outlines

#### Creating an Appearance

1. **Assets > Create > UniText > Appearance**
2. Set **Default Material** — used for all fonts unless overridden
3. Optionally add **Font Materials** — per-font material overrides

```
UniTextAppearance
├── Default Materials: [UniText-SDF]
└── Font Materials:
    ├── NotoSansArabic → [UniText-SDF-2Pass-Outline, UniText-SDF-2Pass-Face]
    └── HeaderFont → [UniText-SDF-Glow]
```

Material resolution order:
1. Font-specific override (if set) — exact match by font asset
2. Default materials — fallback for any font without an override
3. Emoji fonts always use their own built-in material automatically

#### Material Pass Count

- **1 material** — standard single-pass rendering
- **2 materials** — 2-pass rendering: `[0]` = outline/shadow pass, `[1]` = face pass. Essential for connected scripts

#### Best Practices

- **One Appearance per visual style** — create separate appearances for "body text", "heading", "caption", etc.
- **Share across components** — Appearance is a ScriptableObject. Assign the same asset to hundreds of components. Change the material once — all update
- **Arabic/Persian always use 2-pass** — single-pass outlines create visible seams between connected letters
- **Don't duplicate materials** — if all fonts use the same shader settings, just set the Default Material

### 2.5 Font Tools Window

Open via **Tools > UniText > Font Tools**. Two tabs:

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

---

## 3. Markup System

UniText features an extensible markup system based on **Modifiers** and **Parse Rules**.

### 3.1 Architecture: Rule + Modifier

The system separates **what to parse** from **what to do**:

- **Parse Rule** (`IParseRule`) — finds patterns in text and produces ranges
- **Modifier** (`BaseModifier`) — applies a visual or structural effect to those ranges

This separation means:
- One modifier can work with multiple parse rules
- One parse rule can trigger different modifiers
- You can create custom rules and modifiers independently

**Example**: BoldModifier can work with any rule that detects "bold" intent:

| Parse Rule | Syntax | Modifier |
|------------|--------|----------|
| BoldParseRule (built-in) | `<b>bold</b>` | BoldModifier |
| Your MarkdownBoldRule | `**bold**` | BoldModifier |
| Your BBCodeRule | `[b]bold[/b]` | BoldModifier |

### 3.2 Built-in Tags

| Tag | Modifier | Parse Rule | Example |
|-----|----------|------------|---------|
| `<b>` | BoldModifier | BoldParseRule | `<b>bold</b>` |
| `<i>` | ItalicModifier | ItalicParseRule | `<i>italic</i>` |
| `<u>` | UnderlineModifier | UnderlineParseRule | `<u>underline</u>` |
| `<s>` | StrikethroughModifier | StrikethroughParseRule | `<s>strike</s>` |
| `<color>` | ColorModifier | ColorParseRule | `<color=#FF0000>red</color>` |
| `<size>` | SizeModifier | SizeParseRule | `<size=24>large</size>` |
| `<gradient>` | GradientModifier | GradientParseRule | `<gradient=rainbow>text</gradient>` |
| `<cspace>` | LetterSpacingModifier | CSpaceParseRule | `<cspace=5>wider</cspace>` |
| `<line-height>` | LineHeightModifier | LineHeightParseRule | `<line-height=1.5>text</line-height>` |
| `<line-spacing>` | LineHeightModifier | LineSpacingParseRule | `<line-spacing=10>text</line-spacing>` |
| `<upper>` | UppercaseModifier | UppercaseParseRule | `<upper>text</upper>` |
| `<ellipsis>` | EllipsisModifier | EllipsisTagRule | `<ellipsis=1>long text</ellipsis>` |
| `<li>` | ListModifier | (tag-based) | `<li>bullet item</li>` |
| `<link>` | LinkModifier | LinkTagParseRule | `<link=url>click</link>` |
| `<obj>` | ObjModifier | ObjParseRule | `<obj=icon/>` |

#### Markdown-Style Rules

| Parse Rule | Syntax | Modifier |
|------------|--------|----------|
| MarkdownLinkParseRule | `[text](url)` | LinkModifier |
| MarkdownListParseRule | `- item`, `* item`, `1. item` | ListModifier |
| RawUrlParseRule | Auto-detects `https://...` URLs | LinkModifier |

#### Utility Rules

| Parse Rule | Purpose | Modifier |
|------------|---------|----------|
| RangeRule | Apply modifier to specific ranges without markup | Any |
| StringParseRule | Match literal string patterns | Any |
| CompositeParseRule | Combine multiple rules into one | Any |

### 3.3 Parameter Formats Reference

**Color:**
- Hex: `#RGB`, `#RRGGBB`, `#RRGGBBAA`
- Named (20 colors): white, black, red, green, blue, yellow, cyan, magenta, orange, purple, gray, lime, brown, pink, navy, teal, olive, maroon, silver, gold

**Size:**
- Absolute: `<size=24>` — 24 pixels
- Percentage: `<size=150%>` — 150% of base size
- Relative: `<size=+10>` / `<size=-5>` — offset from base

**Gradient:**
- Default: `<gradient=name>` — horizontal (0 degrees)
- Angled: `<gradient=name,45>` — rotated (0=right, 90=up)
- Logical: `<gradient=name,L>` — by character index, not visual position

Gradients are defined in the **UniTextGradients** asset (Project Settings > UniText > Gradients).

**Letter spacing:**
- Pixels: `<cspace=5>` — 5px extra spacing
- Em units: `<cspace=0.1em>` — 0.1 em extra spacing

**Ellipsis (text truncation):**
- `<ellipsis=1>` — truncate end (default): `Hello Wo...`
- `<ellipsis=0>` — truncate start: `...o World`
- `<ellipsis=0.5>` — truncate middle: `Hel...rld`
- Any float 0-1 for fine-grained control

### 3.4 Adding Modifiers to a Component

#### In the Inspector

1. Expand **Mod Registers** list on the UniText component
2. Click **+** to add an entry
3. Select a **Rule** (e.g., ColorParseRule)
4. Select a **Modifier** (e.g., ColorModifier)

Each entry is a Rule+Modifier pair. Tags from the Rule are parsed in text, and the Modifier applies the effect to matched ranges.

#### Via Code

```csharp
uniText.RegisterModifier(new ModRegister
{
    Rule = new ColorParseRule(),
    Modifier = new ColorModifier()
});
```

Remove at runtime:

```csharp
bool removed = uniText.UnregisterModifier(modRegister);

// Or remove all:
uniText.ClearModifiers();
```

### 3.5 ModRegisterConfig — Shared Configuration

**Problem:** You have 50 UniText components that all need the same set of modifiers (bold, italic, color, links). Setting up each one manually is tedious and error-prone.

**Solution:** ModRegisterConfig is a ScriptableObject that stores a reusable list of Rule+Modifier pairs.

#### Setup

1. **Assets > Create > UniText > Mod Register Config**
2. Add your modifier pairs:

```
MyModConfig.asset
├── [0] BoldModifier + BoldParseRule
├── [1] ItalicModifier + ItalicParseRule
├── [2] ColorModifier + ColorParseRule
├── [3] LinkModifier + LinkTagParseRule
└── [4] UnderlineModifier + UnderlineParseRule
```

3. On each UniText component, add this config to the **Mod Register Configs** list

#### Benefits

- **Single source of truth** — change the config, all components update
- **No duplication** — define modifiers once, reference everywhere
- **Combinable** — a component can have multiple configs plus its own local ModRegisters. They all work together
- **Version control friendly** — one asset to track rather than per-component settings

#### Local vs Config

| Feature | Local Mod Registers | ModRegisterConfig |
|---------|-------------------|-------------------|
| Scope | Per-component | Shared across components |
| Edit location | UniText Inspector | Config asset Inspector |
| Use case | Component-specific markup | Project-wide standard markup |

A component's effective set of modifiers = its local Mod Registers + all Mod Register Configs.

### 3.6 RangeRule — Applying Modifiers Without Markup

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

uniText.RegisterModifier(new ModRegister
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

uniText.RegisterModifier(new ModRegister
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

### 3.7 StringParseRule — Literal Pattern Matching

StringParseRule matches literal string patterns in text (no XML/HTML syntax):

```csharp
var emojiRule = new StringParseRule();
emojiRule.patterns = new[] { ":)", ":(", ":D" };
emojiRule.hasReplacement = true;
emojiRule.replacement = "😊";

uniText.RegisterModifier(new ModRegister
{
    Rule = emojiRule,
    Modifier = new EmptyModifier()  // no visual effect, just replacement
});
// ":)" in text gets replaced with "😊"
```

### 3.8 CompositeParseRule — Combining Rules

CompositeParseRule groups multiple rules into one. It tries child rules in order and returns the first match:

```csharp
var composite = new CompositeParseRule();
composite.rules.Add(new LinkTagParseRule());      // <link=url>text</link>
composite.rules.Add(new MarkdownLinkParseRule()); // [text](url)
composite.rules.Add(new RawUrlParseRule());       // auto-detect https://...

uniText.RegisterModifier(new ModRegister
{
    Rule = composite,
    Modifier = new LinkModifier()
});
// All three link syntaxes work with a single modifier
```

### 3.9 Priority System

Parse rules have a `Priority` property that controls matching order (higher = matched first):

| Priority | Use Case | Example |
|----------|----------|---------|
| Positive (e.g., 10) | Explicit markup should match before anything else | Custom rules |
| 0 (default) | Standard tag-based and markdown rules | BoldParseRule, MarkdownLinkParseRule |
| Negative (e.g., -100) | Auto-detection, should only match if nothing else did | RawUrlParseRule (-100) |

This prevents conflicts: `<link=url>https://example.com</link>` won't be double-matched by both LinkTagParseRule and RawUrlParseRule.

### 3.10 Creating Custom Parse Rules

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

**Simplest approach — extend TagParseRule:**

If your syntax follows the `<tag>content</tag>` pattern, just extend TagParseRule — all parsing logic is handled for you:

```csharp
[Serializable]
public sealed class MyCustomTagRule : TagParseRule
{
    protected override string TagName => "highlight";
    protected override bool HasParameter => true;
    // Now <highlight=yellow>text</highlight> works automatically
}
```

### 3.11 Creating Custom Modifiers

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
| `FontStack` | UniTextFontStack | — | Font collection with fallback chain |
| `Appearance` | UniTextAppearance | — | Material configuration |
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
    uniText.RegisterModifier(new ModRegister
    {
        Modifier = linkModifier,
        Rule = new LinkTagParseRule()
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
uniText.RegisterModifier(new ModRegister
{
    Modifier = new LinkModifier(),
    Rule = new MarkdownLinkParseRule()
});
uniText.Text = "Visit [our website](https://example.com) for details.";

// Auto-detect raw URLs
uniText.RegisterModifier(new ModRegister
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

uniText.RegisterModifier(new ModRegister
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