<div align="center">

# UniText Platinum

**Industrial-grade Unicode 17.0 text engine for Unity**

Built on [HarfBuzz](https://harfbuzz.github.io/) — the same shaping engine behind Chrome, Firefox, Adobe InDesign, and Android.

[![Unity](https://img.shields.io/badge/Unity-2021.3+-black?logo=unity)](https://unity.com/)
[![License](https://img.shields.io/badge/license-PolyForm_Noncommercial-green)](LICENSE.md)
[![Discord](https://img.shields.io/discord/1474286776884396055?color=5865F2&logo=discord&logoColor=white&label=Discord)](https://discord.gg/ynRHp3wRmb)

**891,757** Unicode conformance tests passed. Zero failures.

<img width="871" alt="UniText showcase" src="https://github.com/user-attachments/assets/4f89b2a7-4f7e-4eb0-aa4e-246879672e7a" />

</div>

---

## Key Features

| | Feature | Description |
|---|---|---|
| 🌐 | **150+ Languages** | Arabic, Hebrew, Hindi, Thai, CJK, and every other Unicode script. One component, automatic font fallback |
| 🔄 | **Full BiDi** | Mixed LTR/RTL with numbers and punctuation renders correctly (UAX #9) |
| 😀 | **Native Color Emoji** | ZWJ sequences, skin tones, flags via system fonts. Zero extra build size |
| ⚡ | **SDF & MSDF Rendering** | Curve-based Burst-compiled rasterization. Shared `Texture2DArray` atlas with adaptive tile sizes, reference counting, and LRU eviction |
| 🔤 | **Font Families** | CSS §5.2 weight matching, variable font axes (wght, wdth, ital, slnt, opsz), three-tier resolution: variable axes → static faces → synthesis |
| 🎨 | **Effects** | Outline and shadow via multi-pass SDF. `<outline>`, `<shadow>` tags with color, dilate, offset, softness |
| 🏷️ | **Extensible Markup** | 30+ modifier presets, Markdown (`**bold**`, `*italic*`), custom parse rules, shared configurations |
| 👆 | **Interactive Text** | Clickable/hoverable regions with typed events and highlight system |
| 🇹🇭 | **Word Segmentation** | Dictionary-based word breaking for Thai, Lao, Khmer, Myanmar |
| 🌍 | **UniTextWorld** | 3D text without Canvas — full pipeline via MeshRenderer |
| 📦 | **Zstd Compression** | ~2.7x smaller builds for Latin/Arabic, ~1.3x for CJK |
| 🛠️ | **Editor UX** | Style selector with ~30 presets, font family inspector, glyph picker, atlas preview |

<div align="center">
<img width="2157" alt="Languages showcase" src="https://github.com/user-attachments/assets/81b9bcba-fa6d-4e50-8e7d-2781a7d0c38d" />
</div>

## Get UniText

<div align="center">

| | Channel | What you get |
|---|---|---|
| [![Asset Store](https://img.shields.io/badge/Asset_Store-black?logo=unity&logoColor=white)](https://assetstore.unity.com/packages/tools/gui/unitext-357844) | [**Unity Asset Store**](https://assetstore.unity.com/packages/tools/gui/unitext-357844) | Import into `Assets/` folder |
| [![Light Side](https://img.shields.io/badge/Light_Side-direct-blue)](https://unity.lightside.media/unitext/pricing) | [**Direct from Light Side**](https://unity.lightside.media/unitext/pricing) | Install as a **Package** — updates via Package Manager. One-time, **not per seat** |

</div>

## Supported Platforms

| Platform | Architectures |
|----------|---------------|
| Windows  | x86, x64, ARM64 |
| macOS    | x64, Apple Silicon |
| Linux    | x64 |
| Android  | ARMv7, ARM64, x86, x64 |
| iOS      | ARM64 |
| WebGL    | 2.0 |

## Graphics API Support

| Graphics API | Native GPU Upload |
|---|---|
| Direct3D 11 | ✓ |
| Direct3D 12 | ✓ |
| Vulkan | ✓ |
| Metal | ✓ |
| OpenGL Core | ✓ |
| OpenGL ES 3 | ✓ |
| WebGL 2.0 | ✓ |

Render pipelines: **Built-in**, **URP**, **HDRP**

<div align="center">
<img width="1666" alt="Platforms showcase" src="https://github.com/user-attachments/assets/46940f69-103b-406c-8667-e5500e00c579" />
</div>

## Documentation

- [Getting Started](Documentation/GettingStarted.md)
- [Online Documentation](https://unity.lightside.media/unitext/docs/)
- [Website](https://unity.lightside.media/unitext)

## License

UniText is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE.md).

Commercial use requires a license via [Asset Store](https://assetstore.unity.com/packages/tools/gui/unitext-357844) or [direct purchase](https://unity.lightside.media/unitext/pricing).

> [!TIP]
> **Contact: unity@lightside.media** — we respond quickly and licensing is straightforward.

<details>
<summary><b>Third-Party Software</b></summary>
<br>

UniText includes the following open-source libraries in its native plugin. See [Third-Party Notices.txt](Third-Party%20Notices.txt) for full license texts.

| Library | License |
|---------|---------|
| **HarfBuzz** | Old MIT License |
| **FreeType** | FreeType License |
| **Blend2D** | Zlib License |
| **Zstandard** | BSD-3-Clause |
| **zlib** | Zlib License |
| **libpng** | PNG Reference Library License |

Default fonts (Noto Sans, Noto Sans Arabic, Noto Sans Hebrew) — [SIL Open Font License v1.1](http://scripts.sil.org/OFL).
Thai word segmentation dictionary — derived from ICU, [Unicode License V3](http://www.unicode.org/copyright.html).

</details>
