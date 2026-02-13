using System;

namespace LightSide
{
    /// <summary>
    /// Base class for modifiers that create interactive (clickable/hoverable) text ranges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inherit from this class to create custom interactive modifiers like links, hashtags,
    /// mentions, or any other clickable text elements.
    /// </para>
    /// <para>
    /// This class automatically:
    /// <list type="bullet">
    /// <item>Registers/unregisters with <see cref="InteractiveRangeRegistry"/></item>
    /// <item>Stores ranges in a <see cref="PooledArrayAttribute{T}"/></item>
    /// <item>Delegates click/hover events to subclass implementations</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public class HashtagModifier : InteractiveModifier
    /// {
    ///     public override string RangeType => "hashtag";
    ///     public override int Priority => 50;
    ///
    ///     public event Action&lt;string&gt; HashtagClicked;
    ///
    ///     protected override void HandleRangeClicked(InteractiveRange range, TextHitResult hit)
    ///     {
    ///         HashtagClicked?.Invoke(range.data);
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="IInteractiveRangeHandler"/>
    /// <seealso cref="InteractiveRangeRegistry"/>
    /// <seealso cref="LinkModifier"/>
    [Serializable]
    public abstract class InteractiveModifier : BaseModifier, IInteractiveRangeHandler
    {
        private PooledArrayAttribute<InteractiveRange> rangesAttribute;
        private InteractiveRangeRegistry registry;

        /// <summary>Gets the type identifier for ranges created by this modifier.</summary>
        public abstract string RangeType { get; }

        /// <summary>Gets the priority for overlap resolution. Higher values win.</summary>
        public virtual int Priority => 0;

        /// <summary>Gets the attribute key for storing ranges.</summary>
        protected virtual string AttributeKey => $"interactive_{RangeType}";

        /// <inheritdoc/>
        string IInteractiveRangeProvider.RangeType => RangeType;

        /// <inheritdoc/>
        int IInteractiveRangeProvider.Priority => Priority;

        /// <inheritdoc/>
        protected override void OnEnable()
        {
            rangesAttribute ??= buffers.GetOrCreateAttributeData<PooledArrayAttribute<InteractiveRange>>(AttributeKey);
            rangesAttribute.buffer.FakeClear();

            registry = InteractiveRangeRegistry.GetOrCreate(buffers);
            registry.Register(this);
        }

        /// <inheritdoc/>
        protected override void OnDisable()
        {
            registry?.Unregister(this);
        }

        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            registry?.Unregister(this);
            registry = null;
            rangesAttribute = null;
        }

        /// <summary>
        /// Adds an interactive range for the specified text region.
        /// </summary>
        /// <param name="start">Start cluster index (inclusive).</param>
        /// <param name="end">End cluster index (exclusive).</param>
        /// <param name="data">Associated data (URL, tag, ID, etc.).</param>
        protected void AddRange(int start, int end, string data)
        {
            rangesAttribute.Add(new InteractiveRange(start, end, RangeType, data, Priority));
        }

        /// <inheritdoc/>
        public bool TryGetRange(int cluster, out InteractiveRange range)
        {
            if (rangesAttribute != null)
            {
                for (var i = 0; i < rangesAttribute.Count; i++)
                {
                    if (rangesAttribute[i].Contains(cluster))
                    {
                        range = rangesAttribute[i];
                        return true;
                    }
                }
            }

            range = default;
            return false;
        }

        /// <inheritdoc/>
        void IInteractiveRangeHandler.OnRangeClicked(InteractiveRange range, TextHitResult hit)
        {
            HandleRangeClicked(range, hit);
        }

        /// <inheritdoc/>
        void IInteractiveRangeHandler.OnRangeEntered(InteractiveRange range, TextHitResult hit)
        {
            HandleRangeEntered(range, hit);
        }

        /// <inheritdoc/>
        void IInteractiveRangeHandler.OnRangeExited(InteractiveRange range)
        {
            HandleRangeExited(range);
        }

        /// <summary>
        /// Called when the user clicks on a range owned by this modifier.
        /// </summary>
        /// <param name="range">The clicked range.</param>
        /// <param name="hit">The text hit result.</param>
        protected virtual void HandleRangeClicked(InteractiveRange range, TextHitResult hit) { }

        /// <summary>
        /// Called when the pointer enters a range owned by this modifier (desktop only).
        /// </summary>
        /// <param name="range">The range being entered.</param>
        /// <param name="hit">The text hit result.</param>
        protected virtual void HandleRangeEntered(InteractiveRange range, TextHitResult hit) { }

        /// <summary>
        /// Called when the pointer exits a range owned by this modifier (desktop only).
        /// </summary>
        /// <param name="range">The range being exited.</param>
        protected virtual void HandleRangeExited(InteractiveRange range) { }
    }
}
