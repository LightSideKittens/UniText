# UniText

Industrial-grade Unicode 17.0 text engine for Unity. Built on **HarfBuzz** — the same shaping engine used by Chrome, Firefox, Adobe InDesign, and Android.

Passes all **891,757** official Unicode conformance tests. Zero GC allocations at runtime. **3-21x faster** than TextMeshPro.

<img width="871" height="978" alt="image" src="https://github.com/user-attachments/assets/4f89b2a7-4f7e-4eb0-aa4e-246879672e7a" />

## Key Features

- **150+ languages** — Arabic, Hebrew, Hindi, Thai, CJK, and every other Unicode script. One component, automatic font fallback
- **Full BiDi** — mixed LTR/RTL with numbers and punctuation renders correctly (UAX #9)
- **Native color emoji** — ZWJ sequences, skin tones, flags via system fonts. Zero extra build size

<img width="2157" height="1078" alt="image_2026-01-27_02-37-03" src="https://github.com/user-attachments/assets/81b9bcba-fa6d-4e50-8e7d-2781a7d0c38d" />

- **Extensible markup** — 15+ built-in modifiers, custom parse rules, shared configurations
- **Interactive text** — clickable/hoverable regions with typed events and highlight system
- **Zero allocations** — custom pooling, parallel processing, 3-21x faster than TextMeshPro

## Installation

### Unity Package Manager (Git URL)

1. Open **Window > Package Manager**
2. Click **+** > **Add package from git URL...**
3. Enter:
   ```
   https://github.com/LightSideMeowshop/unitext.git
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

## Performance

<img width="629" height="558" alt="image_2026-02-13_18-25-28" src="https://github.com/user-attachments/assets/328bf127-bde1-449b-8e26-b713a8920102" />

Android production build, 100 objects x 2300 chars:

| Benchmark        | vs TextMeshPro | vs UI Toolkit |
|------------------|----------------|---------------|
| Object creation  | 3.4x faster    | 4.9x faster   |
| Full rebuild     | 2.9x faster    | 5.4x faster   |
| Layout           | 20.7x faster   | 3.4x faster   |
| Mesh rebuild     | 7.1x faster    | 2.4x faster   |

- **Zero GC allocations** at runtime — custom pooling with 95%+ hit rate
- Multi-threaded **parallel** text processing
- FreeType glyph rendering: **4.7-15.2x** faster than Unity FontEngine

## Supported Platforms

| Platform | Architectures |
|----------|--------------|
| Windows | x86, x64, ARM64 |
| macOS | x64, Apple Silicon |
| Linux | x64 |
| Android | ARMv7, ARM64, x86, x64 |
| iOS | ARM64 |
| WebGL | 1.0, 2.0 |

<img width="1666" height="1169" alt="image_2026-01-26_04-23-19" src="https://github.com/user-attachments/assets/46940f69-103b-406c-8667-e5500e00c579" />

## Requirements

- Unity 2021 LTS or newer

## Documentation

- [Getting Started](Documentation/GettingStarted.md)
- [Online Documentation](https://unity.lightside.media/unitext/docs/)
- [Website](https://unity.lightside.media/unitext)

## Support

unity@lightside.media

## License

See [LICENSE.md](LICENSE.md)
