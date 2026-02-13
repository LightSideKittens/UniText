using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Base class for text highlighting and selection visualization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inherit from this class to create custom highlight effects for interactive ranges,
    /// text selection, or any other visual feedback.
    /// </para>
    /// <para>
    /// Assign to <see cref="UniText.Highlighter"/> to enable highlighting.
    /// Set to null to disable.
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class TextHighlighter
    {
        protected UniText owner;
        protected bool isInitialized;

        /// <summary>
        /// Initializes the handler with its owner UniText.
        /// </summary>
        public virtual void Initialize(UniText owner)
        {
            this.owner = owner;
            isInitialized = true;
        }

        /// <summary>
        /// Called when an interactive range is clicked.
        /// </summary>
        /// <param name="range">The clicked range.</param>
        /// <param name="bounds">Visual bounds of the range (may be multiple for BiDi text).</param>
        public virtual void OnRangeClicked(InteractiveRange range, List<Rect> bounds) { }

        /// <summary>
        /// Called when pointer enters an interactive range (desktop only).
        /// </summary>
        /// <param name="range">The entered range.</param>
        /// <param name="bounds">Visual bounds of the range.</param>
        public virtual void OnRangeEntered(InteractiveRange range, List<Rect> bounds) { }

        /// <summary>
        /// Called when pointer exits an interactive range (desktop only).
        /// </summary>
        /// <param name="range">The exited range.</param>
        public virtual void OnRangeExited(InteractiveRange range) { }

        /// <summary>
        /// Called when text selection changes (for InputField).
        /// </summary>
        /// <param name="startCluster">Start of selection (cluster index).</param>
        /// <param name="endCluster">End of selection (cluster index).</param>
        /// <param name="bounds">Visual bounds of selection.</param>
        public virtual void OnSelectionChanged(int startCluster, int endCluster, List<Rect> bounds) { }

        /// <summary>
        /// Called every frame for animation updates.
        /// </summary>
        public virtual void Update() { }

        /// <summary>
        /// Cleans up resources when the handler is removed or UniText is destroyed.
        /// </summary>
        public virtual void Destroy()
        {
            isInitialized = false;
            owner = null;
        }
    }
}
