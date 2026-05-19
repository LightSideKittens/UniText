<div align="center">

# UniText Free (Raw Text Engine Core)

**Looking for production use?** [**UniText Platinum**](https://unity.lightside.media/unitext/pricing) adds Enhanced Style Core, SDF/MSDF rendering, Font Families, Variable Fonts, Effects, 3D text, Font Compression, Long-term Support and much more.

<a href="https://unity.lightside.media/unitext/pricing">
  <img src=".github/assets/platinum-cta.svg" alt="Get UniText Platinum" width="340">
</a>

<sub>Also available on the [Unity Asset Store](https://assetstore.unity.com/packages/tools/gui/unitext-357844).</sub>

---

**Open-source Unicode text engine for Unity**

Built on [HarfBuzz](https://harfbuzz.github.io/) — the same shaping engine behind Chrome, Firefox, Adobe InDesign, and Android.

[![Version](https://img.shields.io/badge/version-1.0.0-blue)](https://github.com/LightSideMeowshop/unitext/tree/1.0.0)
[![Unity](https://img.shields.io/badge/Unity-2021.3+-black?logo=unity)](https://unity.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE.md)
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
| 🏷️ | **Extensible Markup** | 15+ built-in modifiers, custom parse rules, shared configurations |
| 👆 | **Interactive Text** | Clickable/hoverable regions with typed events and highlight system |

<div align="center">
<img width="2157" alt="Languages showcase" src="https://github.com/user-attachments/assets/81b9bcba-fa6d-4e50-8e7d-2781a7d0c38d" />
</div>

## Installation

1. Open **Window > Package Manager**
2. Click **+** > **Add package from git URL...**
3. Enter:
   ```
   https://github.com/LightSideMeowshop/unitext.git#1.0.0
   ```

## Quick Start

1. Select any GameObject with **RectTransform**
2. **Add Component > UniText**
3. Type text — it works. Any language, any direction.

```csharp
var uniText = gameObject.AddComponent<UniText>();
uniText.FontStack = myFontStack;
uniText.Appearance = myAppearance;
uniText.Text = "Hello, World!";
uniText.Text = "مرحبا بالعالم";          // Arabic
uniText.Text = "Mixed: Hello עולם World"; // BiDi
uniText.Text = "👨‍👩‍👧‍👦🇯🇵";                    // Emoji
```

## Supported Platforms

| Platform | Architectures |
|----------|---------------|
| Windows  | x86, x64, ARM64 |
| macOS    | x64, Apple Silicon |
| Linux    | x64 |
| Android  | ARMv7, ARM64, x86, x64 |
| iOS      | ARM64 |
| WebGL    | 2.0 |

<div align="center">
<img width="1666" alt="Platforms showcase" src="https://github.com/user-attachments/assets/46940f69-103b-406c-8667-e5500e00c579" />
</div>

## Documentation

- [Getting Started](Documentation/GettingStarted.md)
- [Online Documentation](https://unity.lightside.media/unitext/docs/)
- [Website](https://unity.lightside.media/unitext)

## License

UniText 1.0 is free and open-source under the [License](LICENSE.md).

> [!TIP]
> **Contact: unity@lightside.media** — questions, feedback, commercial inquiries.

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
