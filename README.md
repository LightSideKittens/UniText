<div align="center">

# UniText

**Every language. Any style. No fonts required. Top performance.**

UniText supplies a common text model for labels, input fields, rich editors and virtualized documents in Canvas UI and world space. Capabilities include native emoji, variable and pixel fonts, Font Memory Mapping, rich editing, layered styles and text animation. Modifier, parser, input and decoration extensions compose through the same pipeline. Shared LightSide systems provide reusable infrastructure for authoring and runtime work.

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
| 🌐 | **Every language** | Arabic, Hebrew, Hindi, Thai, CJK, and every other Unicode script. One component, automatic font fallback |
| 💾 | **Font Memory Mapping** | Font files read on demand without a full managed copy. Unused font assets remain compressed |
| 🟪 | **Pixel-perfect fonts** | Automatic grid detection, sharp pixels and grid-aligned effects and animation. Vector pixel fonts and bitmap-only fonts |
| ✍️ | **Rich editing** | Selection, undo/redo, IME, rich clipboard, touch editing, native keyboards, masks, validation and reusable input behaviors |
| 📖 | **Documents** | Lists, ruby, formulas, inline images and prefabs, with virtualized scrolling |
| 🔄 | **Full BiDi** | Mixed LTR/RTL with numbers and punctuation renders correctly (UAX #9) |
| 😀 | **Native Color Emoji** | ZWJ sequences, skin tones, flags via system fonts. Zero extra build size |
| ⚡ | **SDF & MSDF Rendering** | Sharp text and effects across sizes, with shared glyph storage |
| 🔤 | **Font Families** | Font families, real bold and italic faces, and variable weight, width and slant |
| 🎨 | **Style constructors** | Fills, outlines, shadows, glow, gradients, textures, nested effects and reusable presets. Character, word, line and range styling |
| 🏷️ | **Extensible markup** | Reusable modifier presets, Markdown, custom parsing rules and shared style configurations |
| 👆 | **Interactive Text** | Clickable/hoverable regions with typed events and highlight system |
| 🇹🇭 | **Word Segmentation** | Dictionary-based word breaking for Thai, Lao, Khmer, Myanmar |
| 🌍 | **UniTextWorld** | 3D text, styles and interaction without Canvas, with shared world batching |
| 📦 | **Font compression** | Compressed font data, on-demand loading and runtime glyph atlases |
| 🛠️ | **Editor tools** | Style and asset selectors, layer lists, paint and curve editors, font and glyph tools, previews, Undo and prefab support |

<div align="center">
<img width="2157" alt="Languages showcase" src="https://github.com/user-attachments/assets/81b9bcba-fa6d-4e50-8e7d-2781a7d0c38d" />
</div>

## Installation

<div align="center">

| | Channel | Installation |
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


## LightSide ecosystem

UniText and UniShapes share paints, gradients, textures, filters, property editing and the same approach to modifier and layer construction. MoveIt animates exposed component and nested properties. Text, shapes and Lottie animation use common shaders, materials, batching and GPU infrastructure.

Core also supplies state, clocks, interaction routing, geometry tools, workers, pools, serialization and editor controls. Compatible content can share one draw call; materials, textures, masks, sorting and render passes determine the batch boundaries.

[Font storage and pixel-font settings](https://unity.lightside.media/unitext/docs/) are documented alongside platform requirements.

## Documentation

- [Getting Started](Documentation/GettingStarted.md)
- [Online Documentation](https://unity.lightside.media/unitext/docs/)
- [Website](https://unity.lightside.media/unitext)

## License

UniText is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE.md).

Commercial use requires a license via [Asset Store](https://assetstore.unity.com/packages/tools/gui/unitext-357844) or [direct purchase](https://unity.lightside.media/unitext/pricing).

> [!TIP]
> **[Discord](https://discord.gg/ynRHp3wRmb) is the primary channel for questions, bug reports, help and discussion across LightSide.** Email: [unity@lightside.media](mailto:unity@lightside.media).

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
