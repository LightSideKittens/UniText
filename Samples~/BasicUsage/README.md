# Basic Usage Sample

Demonstrates core UniText features with interactive examples.

## Features Demonstrated

### Markup System
- **Bold**: `<b>text</b>`
- **Italic**: `<i>text</i>`
- **Underline**: `<u>text</u>`
- **Strikethrough**: `<s>text</s>`
- **Color**: `<color=#FF0000>text</color>`
- **Size**: `<size=150%>text</size>`
- **Letter Spacing**: `<cspace=10>text</cspace>`

### RTL Languages
- Arabic (العربية)
- Hebrew (עברית)
- Bidirectional mixed text

### Interactive Links
- Click events with `LinkModifier.LinkClicked`
- Hover events with `LinkModifier.LinkEntered` / `LinkModifier.LinkExited`

## Scene Setup

1. Create a Canvas (UI → Canvas)
2. Add two UniText components:
   - **DemoText** — Main text display (center of screen)
   - **StatusText** — Status bar (bottom of screen)
3. Add `BasicUsageExample` script to any GameObject
4. Assign both UniText components to the script

## Controls

- **Space** or **→** — Next example
- **←** — Previous example
- **Click** on links — Opens URL

## Key Code Concepts

### Registering Modifiers at Runtime

```csharp
// Create modifier and rule pair
var register = new ModRegister
{
    Modifier = new ColorModifier(),
    Rule = new ColorParseRule()
};

// Register with UniText component
uniText.RegisterModifier(register);
```

### Handling Link Events

```csharp
// Get LinkModifier from registered modifiers
var linkModifier = new LinkModifier();
uniText.RegisterModifier(new ModRegister { Modifier = linkModifier, Rule = new LinkTagParseRule() });

// Subscribe to link events
linkModifier.LinkClicked += url => Debug.Log($"Clicked: {url}");
linkModifier.LinkEntered += url => Debug.Log($"Hovering: {url}");
linkModifier.LinkExited += () => Debug.Log("Exited link");
```

### Changing Text at Runtime

```csharp
uniText.Text = "<b>Bold</b> and <color=#FF0000>Red</color>";
```

## Scripts

- `BasicUsageExample.cs` — Main example demonstrating runtime API
