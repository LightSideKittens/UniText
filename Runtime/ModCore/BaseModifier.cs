using System;

namespace LightSide
{
    /// <summary>
    /// Base class for all text modifiers that alter text appearance or behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Modifiers are applied to text ranges via markup tags parsed by <see cref="IParseRule"/> implementations
    /// and can modify glyph rendering, colors, animations, or other properties.
    /// </para>
    /// <para>
    /// Lifecycle:
    /// <list type="number">
    /// <item><see cref="SetOwner"/> is called when the modifier is attached to UniText (sets references only)</item>
    /// <item><see cref="Apply"/> is called for each text range - lazy initialization on first call</item>
    /// <item><see cref="Disable"/> is called when text changes</item>
    /// <item><see cref="Destroy"/> is called when the modifier is removed</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <seealso cref="GlyphModifier{T}"/>
    /// <seealso cref="ModRegister"/>
    [Serializable]
    public abstract class BaseModifier
    {
        /// <summary>Reference to the UniText component this modifier is attached to.</summary>
        [NonSerialized] public UniText uniText;
        /// <summary>Access to text processing buffers.</summary>
        [NonSerialized] protected UniTextBuffers buffers;

        [NonSerialized] private bool isInitialized;

        /// <summary>Returns true if this modifier has been initialized (Apply called at least once).</summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Prepares the modifier for a new text cycle.
        /// Creates buffers and clears them. Called once before Apply pass.
        /// </summary>
        public void Prepare()
        {
            if (isInitialized) return;
            OnEnable();
            isInitialized = true;
        }

        /// <summary>
        /// Applies the modifier to a text range.
        /// Requires Prepare() to be called first.
        /// </summary>
        /// <param name="start">Start codepoint index.</param>
        /// <param name="end">End codepoint index (exclusive).</param>
        /// <param name="parameter">Parameter from the tag (e.g., color value).</param>
        public void Apply(int start, int end, string parameter)
        {
            OnApply(start, end, parameter);
        }

        /// <summary>
        /// Sets the owner UniText reference. Does NOT initialize buffers or subscribe to events.
        /// </summary>
        /// <param name="uniText">The UniText component to attach to.</param>
        public void SetOwner(UniText uniText)
        {
            this.uniText = uniText;
            buffers = uniText.Buffers;
        }

        /// <summary>
        /// Deinitializes the modifier and releases resources.
        /// </summary>
        public void Destroy()
        {
            if (isInitialized)
            {
                OnDisable(); 
                OnDestroy();
            }
            
            isInitialized = false;
        }

        /// <summary>
        /// Resets the modifier state for new text.
        /// Unsubscribes from events - will re-subscribe on next Apply().
        /// </summary>
        public void Disable()
        {
            if (!isInitialized) return;
            OnDisable();
            isInitialized = false;
        }

        /// <summary>
        /// Called on the main thread before parallel processing begins.
        /// Override to cache values from Unity APIs that are main-thread-only
        /// (e.g., <c>Material.GetFloat</c>).
        /// </summary>
        public virtual void PrepareForParallel() { }

        /// <summary>Subscribes to UniText events.</summary>
        protected abstract void OnEnable();

        /// <summary>Unsubscribes from UniText events.</summary>
        protected abstract void OnDisable();

        /// <summary>Releases buffers when the modifier is removed.</summary>
        protected abstract void OnDestroy();

        /// <summary>
        /// Implements the modifier's effect on the specified range.
        /// </summary>
        /// <param name="start">Start codepoint index.</param>
        /// <param name="end">End codepoint index (exclusive).</param>
        /// <param name="parameter">Parameter from the tag.</param>
        protected abstract void OnApply(int start, int end, string parameter);
    }

}
