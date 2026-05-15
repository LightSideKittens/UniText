# Changelog

All notable changes to UniText will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - Unreleased

### Added

#### SDF/MSDF Rendering Pipeline

- **GlyphAtlas** (`Runtime/FontCore/GlyphAtlas.cs`): Shared `Texture2DArray`-backed glyph atlas with two singleton instances — one for SDF (`RHalf`) and one for MSDF (`RGBAHalf`). Features adaptive tile sizes (64/128/256 based on glyph complexity), shelf-based packing within 2048x2048 pages, reference counting with LRU eviction, automatic page recycling, and atlas shrinking.
- **SdfGenerator** (`Runtime/FontCore/SdfGenerator.cs`): Burst-compiled `IJobParallelFor` that generates single-channel SDF tiles using contour-seeded vector propagation (8SSEDT). Operates on raw quadratic Bezier curves — no bitmap rasterization.
- **MsdfGenerator** (`Runtime/FontCore/MsdfGenerator.cs`): Burst-compiled `IJobParallelFor` that generates multi-channel SDF tiles in `RGBAHalf` format. Three per-channel seed+propagate passes with tangent carry for pseudo-distance encoding, plus a fourth channel-agnostic error correction pass.
- **SdfCore** (`Runtime/FontCore/SdfCore.cs`): Shared types and reference implementations of SDF/MSDF algorithms — `GlyphTask` struct (used by both generators), tile transforms, Y-monotone splitting, winding number computation, 8SSEDT propagation (with and without tangent), Newton refinement, and quadratic solver. Both `SdfJob` and `MsdfJob` inline their own copies of the algorithms for optimal Burst codegen.
- **GlyphCurveCache** (`Runtime/FontCore/GlyphCurveCache.cs`): Per-font lazy extraction of glyph outlines as quadratic Bezier segments via FreeType `OutlineDecompose`. Normalizes curves to [0,1] glyph space, computes per-contour winding, runs edge coloring, and sorts segments by Y. Includes a thread-safe FreeType face pool for parallel extraction.
- **EdgeColoring** (`Runtime/FontCore/EdgeColoring.cs`): Port of msdfgen's `edgeColoringSimple` — assigns per-edge RGB channel masks for MSDF rendering. Detects corners via cross/dot product thresholds and cycles colors at corner vertices. Computes bisector vectors and corner flags for each segment.
- **RenderMode** enum on `UniText` component: `SDF` (single-channel) or `MSDF` (multi-channel) — controls which atlas mode the component uses.
- **SDF Detail Multiplier** on `UniTextFont`: Controls tile size classification — higher values force larger atlas tiles for fonts with thin strokes (e.g. calligraphic).
- **Glyph Overrides** on `UniTextFont`: Per-glyph tile size overrides (Auto/64/128/256) for fine-tuning quality on specific glyphs.

#### Font Family Architecture

- **FontFamily struct** on `UniTextFontStack`: `families[]` array replaces old flat `fonts` + `variants` lists. Each family has a `primary` font and optional `faces[]` (bold, italic, light, etc.) with a pre-computed `FontFaceLookup` for fast weight/style matching.
- **FontFaceLookup**: Sorted weight arrays, variable font slots (upright + italic), CSS §5.2 weight matching via BinarySearch. Pre-computed at initialization.
- **Variable font support**: `VariationModifier` with `<var>` tag for direct axis control (wght, wdth, ital, slnt, opsz). `UniTextFont.VariableAxes` exposes axis metadata. `IsVariable` property. Variable font axis enumeration via HarfBuzz (`hb_ot_var_get_axis_infos`) and variation setting via `hb_font_set_variations`.
- **Three-tier face resolution** in `ResolveFontFaces()`: (1) Variable font axes — if font has wght/ital/slnt, set axes directly; (2) Static font face — CSS §5.2 weight matching via `FontFaceLookup.FindFace()`; (3) Synthesis — fake bold/italic buffers remain non-zero for shader-based synthesis.
- **`<b>`/`<i>` semantic tags**: Automatically resolve to variable axes when available, fall back to static faces, then to synthesis. `<var>` tag provides direct axis control without fallback.
- **CSS font-weight scale for bold**: `BoldModifier` uses weight scale 100-900 encoded as a byte per codepoint. Smart default: `max(700, baseWeight + 300)`. Explicit parameter: `<b=500>` for CSS weight 500. Fake bold applied via SDF shader dilate (`UV1.y`) and per-glyph advance correction using FreeType's embolden ratio (em/24).
- **Variation run tracking**: `VariationRunInfo` struct and `variationMap` dictionary in TextProcessor track per-run axis values. `Shaper.Shape()` accepts `HB.hb_variation_t[]` parameter. FreeType coordinates set via `FT.SetVarDesignCoordinates()`.
- **FaceInfo auto-population** (editor): `familyName`, `styleName`, `weightClass`, and `isItalic` are automatically extracted from font data via FreeType on `OnEnable`/`OnValidate` and kept in sync. Fields are read-only in the inspector.
- **Native variable font API**: HarfBuzz axis enumeration/variation setting and FreeType Multiple Masters support (`FT.GetMMVar`, `FT.SetVarDesignCoordinates`) in `FT.cs` and `HB.cs`.

#### Word Segmentation for SE Asian Scripts

- **WordSegmentationProcessor** (`Runtime/Unicode/WordBreak/WordSegmentationProcessor.cs`): Post-processes UAX#14 line breaks — dispatches contiguous SA-class script runs (Thai, Lao, etc.) to registered word segmenters.
- **BestPathSegmenter** (`Runtime/Unicode/WordBreak/BestPathSegmenter.cs`): Dictionary-based best-path (maximal matching) DP algorithm — same approach as ICU Thai. Inserts `Optional` break opportunities at word boundaries.
- **DoubleArrayTrie** (`Runtime/Unicode/WordBreak/DoubleArrayTrie.cs`): Read-only compact double-array trie for fast dictionary lookup. Thread-safe after construction.
- **WordSegmentationDictionary** (`Runtime/Unicode/WordBreak/WordSegmentationDictionary.cs`): ScriptableObject holding compiled trie data for a specific script. Configured via `UniTextSettings.dictionaries[]`.
- **Dictionary Builder** tab in UniText Tools window: Builds dictionary assets from word list text files. Supports drag-and-drop, multi-file selection, target script selection, and automatic trie compilation.

#### Effect System (Outline, Shadow)

- **EffectModifier** (`Runtime/ModCore/EffectModifier.cs`): Abstract base class for modifiers that render an additional effect pass behind the face. Registers `EffectPass` (apply/revert callbacks) on the mesh generator. Provides `RecordEffectGlyph()` to store per-glyph UV and offset data, and `ApplyToMesh()`/`RevertFromMesh()` to write effect data to UV2 channel with vertex position offsets.
- **OutlineModifier** (`Runtime/ModCore/Modifiers/OutlineModifier.cs`): Outline effect via `<outline=dilate>`, `<outline=#color>`, or `<outline=dilate,#color>`. Supports fixed pixel size mode. Defaults: dilate=0.2, color=black.
- **ShadowModifier** (`Runtime/ModCore/Modifiers/ShadowModifier.cs`): Shadow/underlay effect via `<shadow=#color>`, `<shadow=dilate,#color>`, or `<shadow=dilate,#color,offsetX,offsetY,softness>`. Supports vertex shifts for offset shadows and fixed pixel size mode. Defaults: dilate=0, color=black 50% alpha.
- **EffectPacking** (`Runtime/Core/EffectPacking.cs`): Static utility for packing `Color32` into a single `float` via bit reinterpretation for shader unpacking.
- **UV2/UV3 buffers** on `UniTextMeshGenerator`: On-demand allocation of additional UV channels for effect layer data.
- **Multi-pass rendering** in `UniText.UpdateSubMeshes`: Effect passes rendered before the face pass using separate materials (Base shader). Each pass applies and reverts its mesh modifications via callbacks.

#### Material Management

- **UniTextMaterialCache** (`Runtime/Core/UniTextMaterialCache.cs`): Static cache that lazily creates and manages shared materials — SDF Face, SDF Base, MSDF Face, MSDF Base. MSDF variants use the `UNITEXT_MSDF` shader keyword. Subscribes to atlas texture changes and syncs `_MainTex` automatically.
- **Shader references on UniTextSettings**: `requiredShaders[]` array stores references to Base, Face, and Emoji shaders. `GetShader(int index)` provides runtime access. Settings provider auto-populates these on editor load.

#### Tag System Overhaul

- **TagRule** (`Runtime/ModCore/Rules/TagRule.cs`): Universal configurable tag parse rule that replaces all individual per-tag rule classes. A single sealed class with a serialized `tagName` field. Supports `defaultParameter` for fallback values and automatic parameter merging (tag-supplied values take priority, remaining fields filled from default).
- **MarkdownWrapRule** (`Runtime/ModCore/Rules/MarkdownWrapRule.cs`): Parse rule for Markdown-style symmetric wrap markers (`**`, `*`, `~~`, `++`). Configurable marker string, stack-based open/close matching, priority by marker length.
- **Simplified TagParseRule base**: Parameters are now always optional (no `HasParameter` virtual). Self-closing is purely syntax-driven (`<tag/>` or `<tag=value/>`). Removed `HasParameter`, `IsSelfClosing`, `InsertString` virtual properties.
- **DeprecatedTagRules** (`Runtime/ModCore/Rules/DeprecatedTagRules.cs`): All 16 tag parse rule classes (14 old + 2 new for outline/shadow) consolidated as hidden one-liner definitions marked with `[HideFromTypeSelector]` for backward-compatible deserialization.

#### Editor UX

- **Selector** (`Editor/Selector.cs`): Full-featured searchable popup selector with grouped mode (expandable group headers with submenu panels), flat search mode (multi-word tokenized, case-insensitive), keyboard navigation, description panels, theme-aware icons, auto-close on focus loss, and optional search field toggle.
- **Mod Register Presets**: The modifier list in the UniText inspector now opens a `Selector` with ~30 predefined presets (Bold, Italic, Outline, Shadow, Markdown variants, etc.) with icons and descriptions. Presets auto-configure both modifier and parse rule.
- **RangeRuleDataDrawer** (`Editor/RangeRuleDataDrawer.cs`): Custom property drawer for `RangeRule.Data` that generates structured UI for modifier parameters based on `ParameterFieldAttribute` metadata. Supports float, int, color, bool, string, enum, and unit (px/em/%) field types.
- **UniTextFontStackEditor** (`Editor/UniTextFontStackEditor.cs`): Custom inspector for `UniTextFontStack` with a Font Families section — each family displayed as a foldable group with primary font, faces list, family name mismatch warnings, weight/italic labels, add/remove buttons, and drag-and-drop zone.
- **Glyph Picker** in font editor: Type text to preview glyph rendering, select individual glyphs, and add tile size overrides directly from the preview grid.
- **Variable Axes Info** in font editor: Displays detected variable font axis metadata (tag, name, min/default/max) when a variable font is loaded.
- **UniTextObjectMenu** (`Editor/UniTextObjectMenu.cs`): `GameObject/UI/` menu items for creating UniText Text and Button objects. Supports prefab overrides via `UniTextSettings`. Creates Canvas/EventSystem if needed.
- **Atlas preview tabs**: Font editor preview split into SDF, MSDF, and Emoji tabs. Uses a `Hidden/UniText/AtlasPreview` shader to display raw distance field textures (grayscale for SDF, RGB for MSDF) from `Texture2DArray` slices.
- **Theme-aware editor icons**: `UniTextEditorResources` provides tinted icon caching for dark/light theme, with per-group and per-type icon mappings.
- **Text selection highlight**: `DefaultTextHighlighter` gains a `selectionGraphic` for programmatic text selection display via `SetSelection()`/`ClearSelection()`.

#### Metadata Attributes

- **ParameterFieldAttribute** (`Runtime/Attributes/ParameterFieldAttribute.cs`): Declares modifier parameter metadata (order, name, type, default) for auto-generating editor UI. Applied to all parameterized modifiers.
- **TypeDescriptionAttribute** (`Runtime/Attributes/TypeDescriptionAttribute.cs`): Human-readable description for types, shown in the Selector popup. Applied to all modifiers and parse rules.
- **HideFromTypeSelectorAttribute** (`Runtime/Attributes/TypeSelectorAttribute.cs`): Hides a type from the type selector dropdown while keeping it deserializable.

#### Virtual Glyph Injection

- **`virtualPositionedGlyphs` buffer** on `UniTextBuffers`: Separate buffer for glyphs injected by modifiers (ellipsis dots, list markers). Does not affect hit testing or selection.
- **`BeforeGenerateMesh` event** on `UniText`: Raised after glyph positioning but before mesh generation, allowing modifiers to inject virtual glyphs.
- `EllipsisModifier` and `ListModifier` now inject `PositionedGlyph` entries into the virtual buffer instead of drawing directly during mesh generation.

#### UniTextWorld (3D Text Rendering)

- **UniTextWorld** (`Runtime/Core/Component/UniTextWorld.cs`): World-space text rendering component. Provides the same text processing pipeline as `UniText` (Unicode, BiDi, shaping, line breaking, modifiers, emoji, font fallback, variable fonts) but renders via MeshRenderer + MeshFilter instead of CanvasRenderer. No Canvas required.
- **UniTextBase** (`Runtime/Core/Component/UniTextBase.cs`): Extracted shared base class from `UniText` — all text processing, modifier management, dirty flags, lifecycle, and parallel batch pipeline now live in `UniTextBase`. Both `UniText` (Canvas) and `UniTextWorld` (MeshRenderer) inherit from it.
- **UniTextBase_Parallel** (`Runtime/Core/Component/UniTextBase_Parallel.cs`): Extracted parallel batch processing pipeline (component collection, glyph batching, atlas rasterization, mesh generation, apply) from `UniText_Parallel` into a shared base partial class.
- **Per-instance owned sub-meshes**: Each effect pass and face segment renders via a dedicated child GameObject (`-_UTWSM_-`) with its own MeshFilter + MeshRenderer + per-instance Mesh (`HideFlags.HideAndDontSave`). Sorting order controls render layering (effects behind face).
- **Phased mesh upload**: Base vertex data (positions, UV0, UV1, UV3, colors, triangles) written once to all SDF sub-meshes; effect passes then overwrite only changed channels (UV2 + vertex shifts). Skips `Mesh.Clear()` when vertex count is unchanged between frames.
- **UniTextWorldEditor** (`Editor/UniTextWorldEditor.cs`): Custom inspector for `UniTextWorld` with sorting order and sorting layer controls.
- **UniTextBaseEditor** (`Editor/UniTextBaseEditor.cs`): Extracted shared editor base class from `UniTextEditor` for reuse by both `UniTextEditor` and `UniTextWorldEditor`.

#### SmallCaps and Lowercase Modifiers

- **SmallCapsModifier** (`Runtime/ModCore/Modifiers/SmallCapsModifier.cs`): Renders lowercase letters as small capitals. Two-tier approach: (1) Native — activates OpenType `smcp` feature via HarfBuzz for proper small cap glyphs; (2) Synthesis — converts to uppercase and scales down by 0.8x (fallback for fonts without `smcp`). Per-codepoint attribute byte: 0 = unchanged, 1 = native, 2 = synthesis. Synthesis adjusts both vertex positions and shaped glyph advances.
- **LowercaseModifier** (`Runtime/ModCore/Modifiers/LowercaseModifier.cs`): Transforms text to lowercase within marked ranges. Applied during modifier Apply phase before shaping.
- **`smcp` feature detection** in `Shaper`: `HasSmcpFeature()` test-shapes `'a'` with and without `smcp` feature, compares glyph IDs. Result cached per font ID in `smcpSupportCache`.
- **HarfBuzz feature support**: `hb_feature_t` struct and `Shape(font, buffer, features)` overload for passing OpenType features to shaping. `MakeTag()` utility for constructing OpenType tag values.
- **Shaper features parameter**: `Shaper.Shape()` now accepts optional `hb_feature_t[]` for per-run OpenType feature activation (used by SmallCaps for `smcp`).

#### Other

- **UI Creation Prefabs** on `UniTextSettings`: `textPrefab` and `buttonPrefab` fields for customizing `GameObject/UI/` menu item creation.
- **FreeType `OutlineDecompose`**: New native API that decomposes glyph outlines into quadratic Bezier segments in design units, replacing the old SDF bitmap rendering path.
- **FaceInfo extensions**: Added `weightClass` (CSS 100-900 from OS/2 `usWeightClass`) and `isItalic` (from FreeType `style_flags`) to the `FaceInfo` struct.
- **DefaultParameterAttribute** (`Runtime/Attributes/DefaultParameterAttribute.cs`): Declares default parameter values for modifiers, enabling parameter auto-fill in the editor.
- **ParameterFieldUtility** (`Editor/ParameterFieldUtility.cs`): Extracted shared parameter field drawing logic from `RangeRuleDataDrawer` for reuse by `DefaultParameterDrawer` and other editors.
- **Emoji atlas Texture2DArray**: `EmojiFont` now maintains a `Texture2DArray` synced from staging `Texture2D` pages, with incremental dirty-page sync.
- **ColorParsing** (`Runtime/ModCore/ColorParsing.cs`): Shared static utility for parsing hex (#RGB, #RRGGBB, #RRGGBBAA) and 21 named colors. Extracted from `ColorModifier` for reuse by OutlineModifier, ShadowModifier, and RangeRuleDataDrawer.

### Changed

#### UniTextWorld Rendering

- `UniText` component refactored: shared logic (text processing, modifier management, dirty flags, lifecycle, parallel pipeline) extracted to `UniTextBase`. `UniText` retains only Canvas-specific rendering (`CanvasRenderer`, stencil, `UpdateGeometry`).
- `UniText_Parallel` refactored: batch pipeline logic extracted to `UniTextBase_Parallel`. `UniText_Parallel` retains only Canvas-specific click handling.
- Mesh generator callbacks renamed to camelCase: `OnGlyph` → `onGlyph`, `OnAfterPage` → `onAfterPage`, `OnRebuildStart` → `onRebuildStart`, `OnRebuildEnd` → `onRebuildEnd`.
- Mesh generator: removed unused public fields (`currentShapedGlyphIndex`, `x`, `y`, `width`, `xScale`, `atlasSize`, `gradientScale`, `spreadRatio`, `rectWidth`, `hAlignment`, `currentFontId`). `SetHorizontalAlignment()` method removed.
- `UniTextFontProvider`: renamed `MainFont` → `PrimaryFont`, `MainFontId` → `PrinaryFontId`. Internal field names updated accordingly.
- `EmojiFont`: emoji atlas textures now use mipmaps (`Texture2D` and `Texture2DArray` created with `mipmap=true`). Filter mode changed to `Trilinear` with `mipMapBias = -0.5f`. Packing spacing increased from 1 to 4 pixels to prevent mipmap bleeding.
- All modifier base classes updated to use renamed `UniTextBase` references instead of `UniText`.

#### Rendering Pipeline

- Mesh generator rewritten from group-by-font-then-atlas iteration to single-pass loop over all positioned glyphs. SDF glyphs look up tiles in the shared `GlyphAtlas`; emoji glyphs processed separately in `GenerateEmojiSegment`.
- UV encoding changed: UV0.zw = `(tileIdx, glyphH)` for atlas tile lookup; UV1 = `(aspect, faceDilate)` as `Vector2` (was `Vector4`).
- Glyph metrics now use design units directly throughout the pipeline — removed `pointSize`-based `metricsConversion` factor.
- `UniTextRenderData` simplified to carry only mesh and font ID; materials assigned externally via `UniTextMaterialCache`.
- Multi-pass effect rendering in `UpdateSubMeshes`: effect passes render before the face pass, each with apply/revert callbacks modifying UV2 and vertex positions.
- Required canvas shader channels extended to include `TexCoord2` and `TexCoord3` for effect layers.
- Glyph reference counting: `UniText` component tracks `currentGlyphKeys` and calls `AddRef`/`Release` on the atlas, enabling accurate eviction.
- Atlas pre-allocation: estimated tile area per atlas mode calculated before rendering, enabling `GlyphAtlas.PreAllocate()`.
- Periodic atlas maintenance: page recycling every 60 frames, atlas shrinking every 300 frames.
- Mesh generator glyph lookup changed from `fontHash` (int) to `varHash48` (long) — supports variable font axis variation. `variationMap` from buffers used to resolve per-run variation hashes.

#### Font System

- `UniTextFont` no longer owns atlas textures — all atlas management delegated to `GlyphAtlas` singletons.
- Glyph preparation/rendering pipeline rewritten: `PrepareGlyphBatch` filters via `GlyphAtlas.TryGetEntry` and protects existing entries with `AddRef`; `RenderPreparedBatch` extracts curves via `GlyphCurveCache` (supports parallel extraction); `PackRenderedBatch` queues segments to `GlyphAtlas.EnsureGlyph`.
- `CreateFontAsset()` simplified — removed `samplingPointSize`, `spreadStrength`, `renderMode`, `atlasSize` parameters.
- `ClearDynamicData()` disposes curve cache and clears font entries from the shared atlas instead of destroying per-font textures.
- `OnDestroy()` now calls `Shaper.ClearCache()` to properly release HarfBuzz native data (was previously leaking).
- `FaceInfo.pointSize` removed; replaced by `weightClass` and `isItalic` fields.
- HarfBuzz memory: `Shaper.FontCacheEntry` now pins the managed `byte[]` via `GCHandle` instead of copying to unmanaged memory via `Marshal.AllocHGlobal`, eliminating the duplicate font data in memory.
- Glyph lookup key changed from `uint glyphIndex` to `long glyphKey` (48-bit variation hash + glyph index) via `GlyphAtlas.MakeKey(varHash48, glyphIndex)`. Enables the same font to cache different glyph shapes for different variable font axis values.
- `PrepareGlyphBatch` and `RenderPreparedBatch` now accept `varHash48` and `ftCoords` parameters for variable font rendering. FreeType design coordinates set before glyph extraction.

#### Font Provider

- Removed `Appearance` property and `GetMaterials()` method from `UniTextFontProvider`.
- Constructor no longer takes an `appearance` parameter.
- Constructor now calls `BuildResolvedFamilies()` to flatten the entire fallback chain into a `resolvedFamilies[]` array with `fontIdToFamilyIndex` dictionary for O(1) family lookup.
- `HasVariants`/`FindVariant()` replaced by `HasFaces` property, `GetFamilyIndex(int fontId)` and `GetFamilyLookup(ushort familyIndex)` for direct access to `FontFaceLookup`.

#### Parallel Pipeline

- Font batch key changed from `UniTextFont` reference to `(UniTextFont, RenderModee, varHash48)` struct — variable font runs with different axis values are batched separately.
- Glyph collection no longer filters already-atlased glyphs at collection time.
- `RasterizeGlyphBatches` extracted as a separate method with per-batch timing diagnostics.
- `DoGenerateMeshData` now clears virtual glyphs buffer, invokes `BeforeGenerateMesh`, and passes virtual glyphs alongside regular glyphs to `GenerateMeshDataOnly`.
- `PeriodicAtlasMaintenance()` extracted as a separate static method, called before component processing instead of after.

#### Modifier System

- `BaseLineModifier` refactored: line segment computation extracted into `ComputeLineSegments()`, executed once then rendered per page. No longer restricted to matching the current font. Event hook changed from `OnAfterGlyphsPerFont` to `OnAfterPage`.
- `LineRenderHelper` rewritten from 3-quad atlas-based rendering (12 vertices) to 1-quad tile-based rendering (4 vertices) using `GlyphAtlas.TryGetEntry` for underscore glyph lookup.
- `EllipsisModifier` changed from immediate mesh drawing (`GlyphRenderHelper.DrawString`) to virtual glyph injection into `virtualPositionedGlyphs`. Event hook changed from `OnAfterGlyphsPerFont` to `BeforeGenerateMesh`.
- `ListModifier` changed from immediate mesh drawing to virtual glyph injection, same pattern as `EllipsisModifier`. Parameter separator changed from `:` to `,`.
- `LineHeightModifier` parameter format changed from `s:value` to `s,value` (comma-separated).
- `ColorModifier` color parsing logic extracted to shared `ColorParsing` utility class.
- `ItalicModifier` now skips vertex shear when the resolved font is already natively italic (`FaceInfo.isItalic`).
- `BoldModifier` `ParameterField` format changed from `"int"` to `"int(100,900)"` for range-constrained editor UI.

#### Editor

- `UniTextFontToolsWindow` renamed to `UniTextToolsWindow`; menu item changed to `Tools/UniText Tools`. File list refactored into reusable `DrawFileList()` method.
- Font editor: removed Atlas Settings section (point size, atlas size, spread, render mode). Replaced with Settings section (font scale, SDF detail multiplier). Atlas preview changed from per-font `Texture2D` to shared `Texture2DArray` slices.
- Type selector dropdown replaced by `Selector` popup with icons, descriptions, and group navigation.
- Editor resource path changed from `Icons/{name}` to `UniText/Icons/{name}`.
- Settings provider no longer draws `defaultAppearance`; now draws UI Creation Prefabs and Word Segmentation sections.
- `EmojiFont` material shader changed from `UI/Default` to `UniText/Emoji` (via `UniTextSettings.GetShader`).
- `SearchableSelector` renamed to `Selector` (file and class). Added `showSearch` parameter to `Show()` for hiding the search field.
- Font editor: added Apply/Revert buttons for rebuild-required properties (`sdfDetailMultiplier`, `glyphOverrides`). Changes are staged as pending until explicitly applied.
- `RangeRuleDataDrawer`: shared parameter field drawing logic extracted into `ParameterFieldUtility` for reuse.

### Removed

- **UniTextAppearance** (`Runtime/FontCore/UniTextAppearance.cs`): Deleted. ScriptableObject that mapped fonts to rendering materials with per-frame property delta caching. Material management replaced by `UniTextMaterialCache`.
- **SDF rendering classes from FreeTypeParallel** (`Runtime/FontCore/FreeTypeParallel.cs`): `SdfRenderedGlyph` struct and `SdfGlyphRenderer` class removed. `FreeTypeFacePool` rewritten — SDF bitmap rendering via `FT.RenderSdfGlyph()` removed, class retained for color bitmap/emoji rendering only. SDF generation replaced by curve-based `GlyphCurveCache` + Burst SDF/MSDF jobs.
- **GlyphRenderHelper** (`Runtime/ModCore/Modifiers/GlyphRenderHelper.cs`): Deleted. Immediate glyph mesh generation utility (`DrawGlyph`, `DrawString`, `MeasureString`). Replaced by virtual glyph injection pattern.
- **UniTextRenderMode enum** (`Runtime/FontCore/FontTypes.cs`): Removed (had values: SDF, Smooth, Mono). Replaced by `UniText.RenderModee` enum (SDF, MSDF) on the component.
- **AtlasMode enum** (`Runtime/FontCore/GlyphAtlas.cs`): Removed. `GlyphAtlas.GetInstance()` now takes `UniText.RenderModee` directly.
- **Per-font atlas textures**: `atlasTextures`, `atlasSize`, `spreadStrength`, `atlasRenderMode`, `usedGlyphRects`, `freeGlyphRects`, and shelf packing state removed from `UniTextFont`.
- **FreeType SDF native API**: `ut_ft_set_sdf_spread`, `ut_ft_render_sdf_glyph`, `ut_ft_free_sdf_buffer` P/Invoke declarations and wrappers removed from `FT.cs`.
- **Shader GUIs**: `UniText_BitmapShaderGUI.cs` and `UniText_SDFShaderGUI.cs` deleted (old custom ShaderGUI for bitmap and SDF shader inspectors).
- **Individual tag parse rule files** (14 files): `BoldParseRule.cs`, `ItalicParseRule.cs`, `ColorParseRule.cs`, `SizeParseRule.cs`, `UnderlineParseRule.cs`, `StrikethroughParseRule.cs`, `CSpaceParseRule.cs`, `LineSpacingParseRule.cs`, `LineHeightParseRule.cs`, `GradientParseRule.cs`, `EllipsisTagRule.cs`, `ObjParseRule.cs`, `Link/LinkTagParseRule.cs`, `UppercaseParseRule.cs`. All consolidated into `TagRule` with backward-compatible stubs in `DeprecatedTagRules.cs`.
- **GeneratedMeshSegment struct**: Removed from `UniTextMeshGenerator`. Replaced by `EffectPass` struct for multi-pass rendering.
- **`defaultAppearance`** from `UniTextSettings` and its backup system.
- **`GlyphsByFont`** grouping from `SharedPipelineComponents` (no longer needed with single-pass mesh generation).
- **`sourceFontFilePath`** from `UniTextFont`.
- **`fonts` and `variants`** from `UniTextFontStack`: Flat `StyledList<UniTextFont>` fonts list and `UniTextFont[]` variants array replaced by `FontFamily[]` families.
- **`FindClosestVariant()`** from `UniTextFontStack`: Replaced by `FontFaceLookup.FindFace()` with CSS §5.2 directional weight matching.
- **`CurrentAtlasMode`** property from `UniText`: Removed. `GlyphAtlas.GetInstance()` now takes `RenderMode` directly.

#### Zstd Font Compression

- **Zstd-compressed font data**: Font bytes stored in `UniTextFont` assets are now compressed with Zstandard (level 22) at import time. Decompression is lazy (on first `FontData` access) with zero per-frame cost. Benchmarks: **~600 MB/s on desktop, ~175 MB/s on low-end Android**. Typical Latin font (600 KB) decompresses in <1 ms. Build size reduction: **~2.7x for Latin/Arabic fonts, ~1.3x for CJK fonts**.
- **Zstd native integration**: Decompression (`ut_zstd_decompress`, `ut_zstd_get_frame_content_size`) built into the runtime `unitext_native` library across all platforms (Windows, Linux, macOS, Android, iOS, tvOS, WebGL). Runtime library built with `-DZSTD_BUILD_COMPRESSION=OFF` for minimal size (~80 KB).
- **Editor-only compression**: `ut_zstd_compress` and `ut_zstd_compress_bound` live in `unitext_native_editor` (desktop only). `Zstd.Compress()` is available only under `#if UNITY_EDITOR`.
- **Automatic migration**: `OnValidate` detects uncompressed font data via Zstd magic bytes (`0x28B52FFD`) and compresses in-place. No manual migration step needed.
- **Memory optimization**: In runtime builds, compressed `fontData` is freed after decompression to avoid keeping both copies in memory.
- **Burst dependency**: Added `com.unity.burst` >= 1.6.0 to package dependencies.

### Fixed

- **HarfBuzz memory leak on font destroy**: `UniTextFont.OnDestroy()` now calls `Shaper.ClearCache()` to release HarfBuzz native data (unmanaged font copy, hb_blob, hb_face, hb_font). Previously, these resources leaked in the static `fontCache` until domain reload.
- **Duplicate font data in memory**: HarfBuzz `FontCacheEntry` now pins the managed `byte[]` via `GCHandle` instead of allocating a separate unmanaged copy, halving per-font memory overhead.
- **FontSize minimum too restrictive**: `fontSize`, `minFontSize`, `maxFontSize` setters clamped to `1f` minimum, preventing small text in world-space. Changed minimum to `0.01f`.
- **UniTextSettings resilience**: Fixed settings loss on package reinstallation.
- **Unity 2021/2022 compatibility**: Fixed compiler errors for older Unity versions.
