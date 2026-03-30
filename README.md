# [UniText](https://discord.gg/ZetkAv3S)

> 🚀 **UniText 2.0 — Coming Soon.** The biggest update since the initial release — rewritten from scratch.

<details>
<summary><b>See what's new in 2.0</b></summary>
<br>

🎨 **No more materials.** UniTextAppearance is gone. Effects are configured directly on components and can target individual parts of the text.

🔤 **Variable Fonts.** Full axis support — weight, width, italic, slant, optical size — controllable per-character. Three-tier resolution: variable axes → static faces → synthesis. Same algorithm as CSS. No other game engine has this (except Godot).

⚡ **SDF & MSDF rendering from raw curves.** Runtime Bézier rasterization via Burst. No pre-baked atlases. A single global `Texture2DArray`: 1 draw call instead of N, automatic LRU eviction, adaptive tile sizes by glyph complexity, defragmentation.

👨‍👩‍👧‍👦 **Font Families.** Primary + faces (bold, italic, light). CSS §5.2 weight matching — same algorithm browsers use. Redesigned `UniTextFontStack`.

🌍 **UniTextWorld.** 3D text without Canvas. Full pipeline (Unicode, BiDi, shaping, effects, emoji, fallback) via MeshRenderer.

✨ **Effects.** Outline and shadow with per-character control. Multi-pass rendering. Works in Canvas and World.

🔠 **SmallCaps.** Native OpenType `smcp` via HarfBuzz. Proper glyphs when available, automatic synthesis when not.

🇹🇭 **Thai & Lao word segmentation.** Dictionary-based word breaking (26K Thai words). Correct line breaks for SE Asian scripts.

📦 **Zstd font compression.** ~2.7x smaller builds for Latin/Arabic, ~1.3x for CJK. ~600 MB/s desktop, ~175 MB/s mobile.

📝 **Markdown.** `**bold**`, `*italic*`, `~~strikethrough~~`, `++underline++` — out of the box.

🛠️ **Editor UX.** Style selector with ~30 presets and search. Font family inspector. Glyph picker with live preview. Atlas preview tabs. Styles & Style Presets.

🐛 **...and a mountain of fixes.** Full changelog and migration notes included with the release.

> ⚠️ Major version bump — expect breaking changes from 1.x.

</details>

---

Industrial-grade Unicode 17.0 text engine for Unity. Built on **HarfBuzz** — the same shaping engine used by Chrome, Firefox, Adobe InDesign, and Android.

Passes all **891,757** official Unicode conformance tests. Zero GC allocations at runtime. **3-21x faster** than TextMeshPro.

<img width="871" height="978" alt="image" src="https://github.com/user-attachments/assets/4f89b2a7-4f7e-4eb0-aa4e-246879672e7a" />

## ✨ Key Features

- 🌐 **150+ languages** — Arabic, Hebrew, Hindi, Thai, CJK, and every other Unicode script. One component, automatic font fallback
- 🔄 **Full BiDi** — mixed LTR/RTL with numbers and punctuation renders correctly (UAX #9)
- 😀 **Native color emoji** — ZWJ sequences, skin tones, flags via system fonts. Zero extra build size

<img width="2157" height="1078" alt="image_2026-01-27_02-37-03" src="https://github.com/user-attachments/assets/81b9bcba-fa6d-4e50-8e7d-2781a7d0c38d" />

- ⚡ **SDF & MSDF rendering** — curve-based Burst-compiled glyph rasterization. Shared Texture2DArray atlas with adaptive tile sizes, reference counting, and LRU eviction
- 🔤 **Font Families** — CSS §5.2 weight matching, variable font axes (wght, wdth, ital, slnt, opsz), three-tier resolution: variable axes → static faces → synthesis
- 🎨 **Effects** — outline and shadow via multi-pass SDF rendering. `<outline>`, `<shadow>` tags with color, dilate, offset, softness
- 🏷️ **Extensible markup** — 30+ modifier presets, Markdown support (`**bold**`, `*italic*`), custom parse rules, shared configurations
- 👆 **Interactive text** — clickable/hoverable regions with typed events and highlight system
- 🇹🇭 **Word segmentation** — dictionary-based word breaking for Thai, Lao, Khmer, Myanmar (SE Asian scripts without spaces)
- 🚀 **Zero allocations** — custom pooling, parallel processing, 3-21x faster than TextMeshPro

## 📦 Installation

### Unity Package Manager (Git URL)

1. ⚠️If you have a previous version of UniText installed, remove it first via Window → Package Manager before installing 2.0.0. This is a major update and cannot be installed over the previous version.
2. Open **Window > Package Manager**
3. Click **+** > **Add package from git URL...**
4. Enter:
   ```
   https://github.com/LightSideMeowshop/unitext.git#2.0.0
   ```

## 🚀 Quick Start

1. Select any GameObject with **RectTransform**
2. **Add Component > UniText**
3. Type text — it works. Any language, any direction.

```csharp
var uniText = gameObject.AddComponent<UniText>();
uniText.FontStack = myFontStack;
uniText.Text = "Hello, World!";
uniText.Text = "مرحبا بالعالم";          // Arabic
uniText.Text = "Mixed: Hello עולם World"; // BiDi
uniText.Text = "👨‍👩‍👧‍👦🇯🇵";                    // Emoji
```

## ⚡ Performance

<img width="629" height="558" alt="image_2026-02-13_18-25-28" src="https://github.com/user-attachments/assets/328bf127-bde1-449b-8e26-b713a8920102" />

Android production build, 100 objects x 2300 chars:

| Benchmark        | vs TextMeshPro | vs UI Toolkit |
|------------------|----------------|---------------|
| Object creation  | 3.4x faster    | 4.9x faster   |
| Full rebuild     | 2.9x faster    | 5.4x faster   |
| Layout           | 20.7x faster   | 3.4x faster   |
| Mesh rebuild     | 7.1x faster    | 2.4x faster   |

- 🧹 **Zero GC allocations** at runtime — custom pooling with 95%+ hit rate
- 🧵 Multi-threaded **parallel** text processing
- ⚙️ FreeType glyph rendering: **4.7-15.2x** faster than Unity FontEngine

## 🖥️ Supported Platforms

| Platform | Architectures |
|----------|--------------|
| Windows | x86, x64, ARM64 |
| macOS | x64, Apple Silicon |
| Linux | x64 |
| Android | ARMv7, ARM64, x86, x64 |
| iOS | ARM64 |
| WebGL | 2.0 |

<img width="1666" height="1169" alt="image_2026-01-26_04-23-19" src="https://github.com/user-attachments/assets/46940f69-103b-406c-8667-e5500e00c579" />

## 📋 Requirements

- Unity 2021 LTS or newer

## 📖 Documentation

- [Getting Started](Documentation/GettingStarted.md)
- [Online Documentation](https://unity.lightside.media/unitext/docs/)
- [Website](https://unity.lightside.media/unitext)

## 💬 Support

unity@lightside.media

## ⚖️ License

UniText is source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE.md).

- **Free** for personal projects, learning, research, game jams, open-source, education, and non-profits.
- **Commercial use** (games, apps, tools that generate revenue) requires a commercial license.

**Contact: unity@lightside.media** — we respond quickly and licensing is straightforward.

## 📜 Third-Party Software

UniText includes the following open-source libraries in its native plugin. See [Third-Party Notices.txt](Third-Party%20Notices.txt) for full license texts.

- **HarfBuzz** (Old MIT License) — text shaping engine
- **FreeType** (FreeType License) — font rasterization. *Portions of this software are copyright (c) The FreeType Project (https://freetype.org). All rights reserved.*
- **Blend2D** (Zlib License) — 2D vector rasterization
- **Zstandard** (BSD-3-Clause License) — data compression
- **zlib** (Zlib License) — general-purpose compression
- **libpng** (PNG Reference Library License) — PNG decoding

Default fonts (Noto Sans, Noto Sans Arabic, Noto Sans Hebrew) are licensed under the [SIL Open Font License v1.1](http://scripts.sil.org/OFL).

Thai word segmentation dictionary is derived from ICU and licensed under the [Unicode License V3](http://www.unicode.org/copyright.html).
