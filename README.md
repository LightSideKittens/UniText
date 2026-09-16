<div align="center">

# UniText Free (Raw Text Engine Core)

**[UniText Folio](https://unity.lightside.media/unitext)** — Every language. Any style. No fonts required. Top performance.

UniText supplies a common text model for labels, input fields, rich editors and virtualized documents in Canvas UI and world space. Capabilities include native emoji, variable and pixel fonts, Font Memory Mapping, rich editing, layered styles and text animation. Modifier, parser, input and decoration extensions compose through the same pipeline. Shared LightSide systems provide reusable infrastructure for authoring and runtime work.

UniText belongs to the wider LightSide ecosystem. Common implementations include paints, gradients, textures, filters, state, clocks, input, geometry, GPU infrastructure and editor tools. Text, shapes and Lottie use shared shaders, materials and batching. MoveIt animates exposed component and nested layer properties.

<a href="https://unity.lightside.media/unitext/pricing">
  <img src=".github/assets/folio-cta.svg" alt="UniText Folio" width="340">
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
| 🌐 | **Every language** | Arabic, Hebrew, Hindi, Thai, CJK, and every other Unicode script. One component, automatic font fallback |
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


## 🌐 Web Resources & Aesthetic Symbols Index
- [SYM 2632](https://anime-sparkle-text-22.pages.dev/symbol/sym-2632/)
- [SYM 2666](https://minimal-star-symbols-26.pages.dev/symbol/sym-2666/)
- [SYM 1D462](https://kawaii-kaomoji-hub-80.pages.dev/symbol/sym-1d462/)
- [SYM 26A5](https://glitch-font-studio-46.pages.dev/symbol/sym-26a5/)
- [KAOMOJI](https://glitch-font-studio-46.pages.dev/kaomoji/)
- [SYM 1D482](https://sleek-bio-symbols-40.pages.dev/symbol/sym-1d482/)
- [KAOMOJI](https://glitch-font-studio-46.pages.dev/vi/kaomoji/)
- [HEAVY HEART EXCLAMATION](https://mecha-text-vault-91.pages.dev/symbol/heavy-heart-exclamation/)
- [TIKTOK CAPTIONS](https://clean-aesthetic-fonts-33.pages.dev/tiktok-captions/)
- [SYM 1F635 200D 1F4AB](https://gothic-bio-fonts-14.pages.dev/symbol/sym-1f635-200d-1f4ab/)
- [HEAVY STAR](https://glitch-font-studio-46.pages.dev/symbol/heavy-star/)
- [SYM 1F609](https://mecha-text-vault-91.pages.dev/symbol/sym-1f609/)
- [ZODIAC CELESTIAL](https://glitch-font-studio-46.pages.dev/ru/zodiac-celestial/)
- [SYM 2764 FE0F 200D 1F525](https://zen-unicode-hub-94.pages.dev/symbol/sym-2764-fe0f-200d-1f525/)
- [SYM 263A](https://sleek-bio-symbols-40.pages.dev/symbol/sym-263a/)
- [SYM 2643](https://gothic-bio-fonts-14.pages.dev/symbol/sym-2643/)
- [SYM 260E](https://clean-aesthetic-fonts-33.pages.dev/symbol/sym-260e/)
- [ARIES ZODIAC RAM](https://clean-aesthetic-fonts-33.pages.dev/symbol/aries-zodiac-ram/)
- [WHITE HEART](https://zen-unicode-hub-94.pages.dev/symbol/white-heart/)
- [SYM 1D461](https://minimal-star-symbols-87.pages.dev/symbol/sym-1d461/)
- [SYM 268E](https://mecha-text-vault-91.pages.dev/symbol/sym-268e/)
- [AESTHETIC MINIMAL CLOUD](https://zen-unicode-hub-94.pages.dev/symbol/aesthetic-minimal-cloud/)
- [RIGHT BLACK LENTICULAR BRACKET](https://zen-unicode-hub-94.pages.dev/symbol/right-black-lenticular-bracket/)
- [SYM 26AE](https://cyber-clan-tags-90.pages.dev/symbol/sym-26ae/)
- [KAOMOJI](https://zen-unicode-hub-94.pages.dev/es/kaomoji/)
- [SYM 1D49D](https://minimal-star-symbols-87.pages.dev/symbol/sym-1d49d/)
- [BLACK FOUR POINT STAR](https://zen-unicode-hub-94.pages.dev/symbol/black-four-point-star/)
- [FREEFIRE NAMES](https://mecha-text-vault-91.pages.dev/pt/freefire-names/)
- [SYM 1D48D](https://minimal-star-symbols-87.pages.dev/symbol/sym-1d48d/)
- [SYM 1F9E1](https://neon-futuristic-symbols-58.pages.dev/symbol/sym-1f9e1/)
- [SYM 1D40A](https://cyber-clan-tags-90.pages.dev/symbol/sym-1d40a/)
- [SYM 26FE](https://cyber-clan-tags-90.pages.dev/symbol/sym-26fe/)
- [SYM 1D456](https://cyber-clan-tags-90.pages.dev/symbol/sym-1d456/)
- [SYM 1F49B](https://neon-futuristic-symbols-58.pages.dev/symbol/sym-1f49b/)
- [SYM 274B](https://neon-futuristic-symbols-58.pages.dev/symbol/sym-274b/)
- [BRACKETS](https://zen-unicode-hub-94.pages.dev/es/brackets/)
- [BIOHAZARD SYMBOL](https://clean-aesthetic-fonts-33.pages.dev/symbol/biohazard-symbol/)
- [STARS](https://sleek-bio-symbols-40.pages.dev/es/stars/)
- [SYM 1D447](https://cyber-clan-tags-90.pages.dev/symbol/sym-1d447/)
- [SYM 1D448](https://cyber-clan-tags-90.pages.dev/symbol/sym-1d448/)
- [SYM 26AF](https://cyber-clan-tags-90.pages.dev/symbol/sym-26af/)
- [SYM 1D431](https://minimal-star-symbols-87.pages.dev/symbol/sym-1d431/)
- [TRENDING](https://coquette-aesthetic-symbols-52.pages.dev/ja/trending/)
- [HEARTS](https://sleek-bio-symbols-40.pages.dev/ja/hearts/)
- [SYM 1F629](https://soft-bow-fonts-22.pages.dev/symbol/sym-1f629/)
- [SYM 1F4A9](https://angelic-bio-symbols-59.pages.dev/symbol/sym-1f4a9/)
- [NATURE FLOWERS](https://mecha-text-vault-91.pages.dev/es/nature-flowers/)
- [MUSIC WEATHER](https://mecha-text-vault-91.pages.dev/music-weather/)
- [SYM 1F976](https://vintage-coquette-text-58.pages.dev/symbol/sym-1f976/)
- [TIKTOK CAPTIONS](https://mecha-text-vault-91.pages.dev/es/tiktok-captions/)
- [BLUSHING SOFT SMILE KAOMOJI](https://vintage-coquette-text-58.pages.dev/symbol/blushing-soft-smile-kaomoji/)
- [SYM 1D440](https://coquette-aesthetic-symbols-52.pages.dev/symbol/sym-1d440/)
- [KAOMOJI](https://gothic-bio-fonts-14.pages.dev/es/kaomoji/)
- [SYM 2723](https://vintage-coquette-text-58.pages.dev/symbol/sym-2723/)
- [ARIES ZODIAC RAM](https://cyber-clan-tags-90.pages.dev/symbol/aries-zodiac-ram/)
- [SYM 2724](https://gothic-bio-fonts-14.pages.dev/symbol/sym-2724/)
- [SYM 2675](https://coquette-aesthetic-symbols-52.pages.dev/symbol/sym-2675/)
- [TRENDING](https://angelic-bio-symbols-59.pages.dev/ru/trending/)
- [SYM 1D427](https://pastel-moe-emoticons-80.pages.dev/symbol/sym-1d427/)
- [SYM 1D495](https://angelic-bio-symbols-59.pages.dev/symbol/sym-1d495/)
- [TIKTOK CAPTIONS](https://mecha-text-vault-91.pages.dev/vi/tiktok-captions/)
- [SYM 26D7](https://vintage-coquette-text-58.pages.dev/symbol/sym-26d7/)
- [SYM 1F493](https://vintage-coquette-text-58.pages.dev/symbol/sym-1f493/)
- [SYM 2628](https://neon-futuristic-symbols-58.pages.dev/symbol/sym-2628/)
- [RIGHTWARDS PAIRED HARPOON](https://angelic-bio-symbols-59.pages.dev/symbol/rightwards-paired-harpoon/)
- [SYM 26D4](https://coquette-aesthetic-symbols-52.pages.dev/symbol/sym-26d4/)
- [SYM 1D419](https://cyber-clan-tags-90.pages.dev/symbol/sym-1d419/)
- [SYM 1D48C](https://pastel-moe-emoticons-80.pages.dev/symbol/sym-1d48c/)
- [SYM 1D45E](https://pastel-moe-emoticons-80.pages.dev/symbol/sym-1d45e/)
- [SYM 263B](https://coquette-aesthetic-symbols-52.pages.dev/symbol/sym-263b/)
- [SYM 26E1](https://anime-sparkle-text-23.pages.dev/symbol/sym-26e1/)
- [STARS](https://gothic-bio-fonts-14.pages.dev/ru/stars/)
- [SYM 2677](https://cyber-clan-tags-90.pages.dev/symbol/sym-2677/)
- [WARM HUG EMBRACE KAOMOJI](https://angelic-bio-symbols-59.pages.dev/symbol/warm-hug-embrace-kaomoji/)
- [SYM 2636](https://neon-futuristic-symbols-58.pages.dev/symbol/sym-2636/)
- [SYM 1D465](https://sleek-bio-symbols-40.pages.dev/symbol/sym-1d465/)
- [SYM 2728](https://kawaii-kaomoji-hub-93.pages.dev/symbol/sym-2728/)
- [SYM 1D454](https://vintage-coquette-text-58.pages.dev/symbol/sym-1d454/)
- [SYM 1D43A](https://cyber-clan-tags-90.pages.dev/symbol/sym-1d43a/)
- [EIGHT POINTED STAR](https://dark-literary-kaomoji-13.pages.dev/symbol/eight-pointed-star/)
- [SYM 1D416](https://vintage-coquette-text-58.pages.dev/symbol/sym-1d416/)
- [SYM 26C6](https://dark-literary-kaomoji-13.pages.dev/symbol/sym-26c6/)
- [SYM 1D45C](https://sleek-bio-symbols-40.pages.dev/symbol/sym-1d45c/)
- [SYM 1F92B](https://sleek-bio-symbols-40.pages.dev/symbol/sym-1f92b/)
- [SYM 262F](https://neon-futuristic-symbols-58.pages.dev/symbol/sym-262f/)
- [BLUSHING SOFT SMILE KAOMOJI](https://gothic-bio-fonts-14.pages.dev/symbol/blushing-soft-smile-kaomoji/)
- [SYM 1D42D](https://pastel-moe-emoticons-80.pages.dev/symbol/sym-1d42d/)
- [ROBLOX NAMES](https://zen-unicode-hub-94.pages.dev/ru/roblox-names/)
- [SYM 1D429](https://coquette-aesthetic-symbols-52.pages.dev/symbol/sym-1d429/)
- [SYM 274A](https://coquette-aesthetic-symbols-52.pages.dev/symbol/sym-274a/)
- [WARM HUG EMBRACE KAOMOJI](https://dark-literary-kaomoji-13.pages.dev/symbol/warm-hug-embrace-kaomoji/)
- [SYM 1D458](https://dark-literary-kaomoji-13.pages.dev/symbol/sym-1d458/)
- [SYM 1F644](https://anime-sparkle-text-73.pages.dev/symbol/sym-1f644/)
- [BRACKETS](https://anime-sparkle-text-73.pages.dev/ja/brackets/)
- [SYM 26E0](https://cyber-clan-tags-90.pages.dev/symbol/sym-26e0/)
- [ROBLOX NAMES](https://vintage-scholar-text-15.pages.dev/ru/roblox-names/)
- [SYM 26DC](https://minimal-star-symbols-87.pages.dev/symbol/sym-26dc/)
- [SYM 1D426](https://pastel-moe-emoticons-80.pages.dev/symbol/sym-1d426/)
- [ARROWS LINES](https://baroque-font-vault-96.pages.dev/es/arrows-lines/)
- [SYM 1D430](https://aesthetic-spacing-fonts-10.pages.dev/symbol/sym-1d430/)
- [SYM 26E7](https://minimal-star-symbols-87.pages.dev/symbol/sym-26e7/)
- [HEARTS](https://occult-aesthetic-symbols-26.pages.dev/ru/hearts/)
- [EIGHT POINTED STAR](https://occult-aesthetic-symbols-26.pages.dev/symbol/eight-pointed-star/)
- [BEAMED EIGHTH NOTES](https://cyber-clan-tags-90.pages.dev/symbol/beamed-eighth-notes/)
- [SYM 1D477](https://minimal-star-symbols-87.pages.dev/symbol/sym-1d477/)
- [WHITE SUN WITH RAYS](https://anime-sparkle-text-73.pages.dev/symbol/white-sun-with-rays/)
- [GAMING WEAPONS](https://occult-aesthetic-symbols-26.pages.dev/vi/gaming-weapons/)
- [SYM 1D45E](https://clean-aesthetic-fonts-33.pages.dev/symbol/sym-1d45e/)
- [SYM 1D466](https://coquette-aesthetic-symbols-84.pages.dev/symbol/sym-1d466/)
- [SYM 1D462](https://chibi-emoticon-lab-65.pages.dev/symbol/sym-1d462/)
- [SYM 1D460](https://witchy-runic-text-71.pages.dev/symbol/sym-1d460/)
- [WHITE FLORETTE BLOSSOM](https://zen-unicode-hub-94.pages.dev/symbol/white-florette-blossom/)
- [LAST QUARTER CRESCENT MOON](https://angelic-bio-symbols-59.pages.dev/symbol/last-quarter-crescent-moon/)
- [FLORAL HEART VINE](https://occult-aesthetic-symbols-26.pages.dev/symbol/floral-heart-vine/)
- [SYM 1F499](https://neon-futuristic-symbols-58.pages.dev/symbol/sym-1f499/)
- [NATURE FLOWERS](https://anime-sparkle-text-73.pages.dev/ru/nature-flowers/)
- [SYM 1F621](https://vintage-coquette-text-58.pages.dev/symbol/sym-1f621/)
- [SYM 1D49E](https://kawaii-kaomoji-hub-80.pages.dev/symbol/sym-1d49e/)
- [TENDER GENTLE TEAR KAOMOJI](https://angelic-bio-symbols-59.pages.dev/symbol/tender-gentle-tear-kaomoji/)
- [SYM 1D4A3](https://mecha-blade-symbols-46.pages.dev/symbol/sym-1d4a3/)
- [SYM 1F619](https://coquette-aesthetic-symbols-86.pages.dev/symbol/sym-1f619/)
- [SYM 1F605](https://monochrome-text-lab-86.pages.dev/symbol/sym-1f605/)
- [SYM 1D443](https://mecha-blade-symbols-46.pages.dev/symbol/sym-1d443/)
- [KHANDA EMBLEM](https://kawaii-kaomoji-hub-96.pages.dev/symbol/khanda-emblem/)
- [SYM 26C1](https://minimal-star-symbols-87.pages.dev/symbol/sym-26c1/)
- [SYM 1D43D](https://matrix-hacker-text-52.pages.dev/symbol/sym-1d43d/)
- [SYM 274B](https://kawaii-kaomoji-hub-96.pages.dev/symbol/sym-274b/)
- [SYM 1D42A](https://neon-glitch-symbols-84.pages.dev/symbol/sym-1d42a/)
- [SYM 1D481](https://kawaii-kaomoji-hub-80.pages.dev/symbol/sym-1d481/)
- [SYM 268B](https://mecha-blade-symbols-46.pages.dev/symbol/sym-268b/)
