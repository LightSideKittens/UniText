using System;
using System.Collections.Generic;
using LightSide;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace LightSide.Samples
{
    /// <summary>
    /// Demonstrates core UniText features: markup system, RTL text, links, and runtime API.
    /// </summary>
    /// <remarks>
    /// This example shows how to:
    /// - Register modifiers programmatically at runtime
    /// - Use markup tags for text styling
    /// - Handle link click events
    /// - Display RTL and bidirectional text
    /// </remarks>
    public class BasicUsageExample : MonoBehaviour
    {
        [Header("UniText Components")]
        [SerializeField] private UniText demoText;
        [SerializeField] private UniText statusText;

        [Header("Configuration")]
        [SerializeField] private bool registerModifiersOnStart = true;

        private readonly List<ModRegister> registeredModifiers = new();
        private int currentExample;
        private string[] examples;
        private LinkModifier linkModifier;

        private void Start()
        {
            if (demoText == null)
            {
                Debug.LogError("[BasicUsageExample] DemoText is not assigned!");
                return;
            }

            if (registerModifiersOnStart)
            {
                RegisterAllModifiers();
            }

            SetupExamples();
            SetupLinkEvents();
            ShowExample(0);
        }

        /// <summary>
        /// Registers all common modifiers programmatically.
        /// This demonstrates runtime modifier registration API.
        /// </summary>
        private void RegisterAllModifiers()
        {
            // Text styling modifiers
            RegisterModifier(new BoldModifier(), new BoldParseRule());
            RegisterModifier(new ItalicModifier(), new ItalicParseRule());
            RegisterModifier(new UnderlineModifier(), new UnderlineParseRule());
            RegisterModifier(new StrikethroughModifier(), new StrikethroughParseRule());

            // Color, gradient, and size
            RegisterModifier(new ColorModifier(), new ColorParseRule());
            RegisterModifier(new GradientModifier(), new GradientParseRule());
            RegisterModifier(new SizeModifier(), new SizeParseRule());

            // Spacing
            RegisterModifier(new LetterSpacingModifier(), new CSpaceParseRule());
            RegisterModifier(new LineHeightModifier(), new LineHeightParseRule());

            // Links (clickable text)
            linkModifier = new LinkModifier();
            RegisterModifier(linkModifier, new LinkTagParseRule());
        }

        private void RegisterModifier(BaseModifier modifier, IParseRule rule)
        {
            var register = new ModRegister
            {
                Modifier = modifier,
                Rule = rule
            };
            demoText.RegisterModifier(register);
            registeredModifiers.Add(register);
        }

        private void SetupExamples()
        {
            examples = new[]
            {
                // 1. Welcome - first impression with emoji
                "✨ <b><color=#FFD700>UniText</color></b> ✨\n<color=#888>Professional Unicode Text Rendering</color>\n\n👉 Press <b>Space</b> or ⬅️➡️ to explore",

                // 2. Text styling showcase
                "<b>Bold</b> • <i>Italic</i> • <u>Underline</u> • <s>Strike</s>\n<b><i>Bold Italic</i></b> • <b><u>Bold Underline</u></b>\n\n🎨 Mix styles: <b><i><color=#FF6B6B>Bold Italic Red</color></i></b>",

                // 3. Color palette
                "🌈 <color=#FF6B6B>Red</color> <color=#FFE66D>Yellow</color> <color=#4ECDC4>Teal</color> <color=#45B7D1>Blue</color> <color=#A06CD5>Purple</color>\n\n<color=#FF6B6B>⁜</color><color=#FF8E6B>⁜</color><color=#FFB06B>⁜</color><color=#FFD26B>⁜</color><color=#FFF46B>⁜</color><color=#D2FF6B>⁜</color><color=#6BFF6B>⁜</color><color=#6BFFD2>⁜</color><color=#6BD2FF>⁜</color><color=#6B8EFF>⁜</color><color=#8E6BFF>⁜</color>",

                // 4. Gradients - Visual mode
                "🎨 <b>Gradient Text - Visual Mode</b>\n\n<gradient=rainbow>Horizontal Rainbow</gradient>\n\n<gradient=ocean,90>Vertical Rainbow</gradient>\n\n<gradient=rainbow,45>Diagonal\nRainbow</gradient>\n\n<gradient=fire>Fire</gradient> • <gradient=ocean>Ocean</gradient>\n\n<color=#888>Visual mode: gradient by X/Y position</color>",

                // 5. Gradients - Logical mode
                "🎨 <b>Gradient Text - Logical Mode</b>\n\n<gradient=rainbow,L>Rainbow gradient spans\nacross multiple lines smoothly</gradient>\n\n<color=#888>Logical mode: gradient by character index</color>\n<color=#888>Use ,L parameter for multi-line text</color>",

                // 6. Size variations
                "<size=60%>tiny</size> <size=80%>small</size> normal <size=120%>large</size> <size=150%>huge</size>\n\n📏 Dynamic sizing for emphasis",

                // 7. Arabic - full RTL showcase
                "<b>العربية</b>\n\nمرحباً بالعالم! 👋\nهذا نص عربي مع <color=#4ECDC4>ألوان</color> و <b>تنسيق</b>.\n\nأرقام: ٠١٢٣٤٥٦٧٨٩ 🔢",

                // 8. Hebrew
                "<b>עברית</b>\n\nשלום עולם! 👋\nזהו טקסט עברי עם <color=#FF6B6B>צבעים</color> ו<b>עיצוב</b>.\n\nמספרים: 0123456789 🔢",

                // 9. Bidirectional - the real magic
                "🔀 <b>Bidirectional Text</b>\n\nThe word <color=#4ECDC4>مرحبا</color> means \"hello\" in Arabic\nUser <color=#FF6B6B>יוסי כהן</color> sent you a message\nFile: <color=#A06CD5>تقرير_٢٠٢٤.pdf</color> (15 MB)\n\nPrices: $99 | ٩٩ ريال | ₪199\nDate: 25 يناير 2024 | 25 בינואר 2024\n\n<color=#888>⬅️ Automatic direction detection ➡️</color>",

                // 10. Emoji showcase
                "😀 <b>Emoji Support</b> 🎉\n\nFaces: 😀 😃 😄 😁 😆 🥹 😅 😂\nGestures: 👋 👍 👎 👏 🙌 🤝 ✌️\nFlags: 🇺🇸 🇬🇧 🇯🇵 🇰🇷 🇩🇪 🇫🇷 🇮🇹 🇪🇸\nFamily: 👨‍👩‍👧‍👦 👨‍👨‍👧 👩‍👩‍👦",

                // 11. Complex scripts (HarfBuzz)
                "🔤 <b>Complex Scripts</b>\n\n<color=#4ECDC4>Arabic ligatures:</color> لا الله بسم الله\n<color=#FF6B6B>Hindi:</color> नमस्ते दुनिया 🙏\n<color=#A06CD5>Arabic joining:</color> ب‍ ‍ب‍ ‍ب (initial, medial, final)",

                // 12. Interactive links
                "🔗 <b>Interactive Links</b>\n\n📖 <link=https://unity.lightside.media/unitext/docs><color=#45B7D1><u>Documentation</u></color></link> - Full API reference\n🌐 <link=https://unity.lightside.media><color=#A06CD5><u>LightSide</u></color></link> - Our website\n\n<color=#888>Click links to open • Hover to preview</color>",

                // 13. Spacing control
                "<cspace=15>S P A C E D</cspace>\n<cspace=-2>Tight kerning</cspace>\n\n<line-height=150%>Line 1 with\nincreased height\nbetween lines</line-height>",

                // 14. Everything combined - finale
                "🚀 <b><color=#FFD700>UniText</color></b> <size=80%>v1.0</size>\n\n✅ <color=#4ECDC4>Full Unicode</color> support\n✅ <color=#FF6B6B>RTL</color>: العربية עברית\n✅ <color=#A06CD5>Emoji</color>: 😀🎉👨‍👩‍👧‍👦🇺🇸\n✅ <color=#FFE66D>HarfBuzz</color> shaping\n✅ <color=#45B7D1>Markup</color> system\n✅ <gradient=rainbow,L>Gradients</gradient>\n\n<link=start><color=#4ECDC4>▶️ Start using UniText</color></link>"
            };
        }

        private void SetupLinkEvents()
        {
            if (linkModifier != null)
            {
                linkModifier.AutoOpenUrl = false; // We handle URL opening ourselves
                linkModifier.LinkClicked += OnLinkClicked;
                linkModifier.LinkEntered += OnLinkEnter;
                linkModifier.LinkExited += OnLinkExit;
            }
        }

        private bool wasPressed;

        private void Update()
        {
            var leftPressed = false;
            var rightPressed = false;
            
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            // Только новая система
            var kb = Keyboard.current;
            if (kb != null)
            {
                leftPressed = kb.leftArrowKey.isPressed || kb.aKey.isPressed;
                rightPressed = kb.rightArrowKey.isPressed || kb.dKey.isPressed;
            }

#elif ENABLE_LEGACY_INPUT_MANAGER
            leftPressed = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A);
            rightPressed = Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D);
#endif

            if (leftPressed)
            {
                if (!wasPressed)
                {
                    PreviousExample(); 
                    wasPressed = true;
                }
            }
            else if (rightPressed)
            {
                if (!wasPressed)
                {
                    NextExample();
                    wasPressed = true;
                }
            }
            else
            {
                wasPressed = false;
            }
        }

        public void NextExample()
        {
            currentExample = (currentExample + 1) % examples.Length;
            ShowExample(currentExample);
        }

        public void PreviousExample()
        {
            currentExample = (currentExample - 1 + examples.Length) % examples.Length;
            ShowExample(currentExample);
        }

        private readonly char[] setTextBuffer = new char[512];

        private void ShowExample(int index)
        {
            if (index % 2 == 0)
            {
                // Even examples: use Text property (string path)
                demoText.Text = examples[index];
                Debug.Log($"[SetText Test] Text setter → Text = \"{demoText.Text}\"");
            }
            else
            {
                // Odd examples: use SetText (char[] path, zero-alloc)
                var src = examples[index];
                src.CopyTo(0, setTextBuffer, 0, src.Length);
                demoText.SetText(setTextBuffer, 0, src.Length);
                Debug.Log($"[SetText Test] SetText(char[]) → Text = \"{demoText.Text}\"");
            }

            UpdateStatus($"Example {index + 1}/{examples.Length} ({(index % 2 == 0 ? "Text" : "SetText")}) - Press Arrow keys");
        }

        private void OnLinkClicked(string url)
        {
            UpdateStatus($"<color=#2ECC71>Clicked:</color> {url}");

            // Open URL if it's a valid web address
            if (url.StartsWith("http"))
            {
                Application.OpenURL(url);
            }
        }

        private void OnLinkEnter(string url)
        {
            UpdateStatus($"<color=#3498DB>Hovering:</color> {url}");
        }

        private void OnLinkExit()
        {
            UpdateStatus($"Example {currentExample + 1}/{examples.Length}");
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.Text = message;
            }
        }

        private void OnDestroy()
        {
            // Clean up event subscriptions
            if (linkModifier != null)
            {
                linkModifier.LinkClicked -= OnLinkClicked;
                linkModifier.LinkEntered -= OnLinkEnter;
                linkModifier.LinkExited -= OnLinkExit;
            }

            // Unregister modifiers we registered
            foreach (var register in registeredModifiers)
            {
                demoText?.UnregisterModifier(register);
            }
            registeredModifiers.Clear();
        }
    }
}
