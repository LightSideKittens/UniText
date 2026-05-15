using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Central registry for interactive text ranges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stores references to all <see cref="IInteractiveRangeProvider"/> instances for a UniText component.
    /// Used by <see cref="UniText"/> to perform hit testing against interactive ranges.
    /// </para>
    /// <para>
    /// Stored in <see cref="UniTextBuffers"/> as <see cref="IAttributeData"/>.
    /// </para>
    /// </remarks>
    /// <seealso cref="IInteractiveRangeProvider"/>
    /// <seealso cref="IInteractiveRangeHandler"/>
    public sealed class InteractiveRangeRegistry : IAttributeData
    {
        /// <summary>Key for storing this registry in UniTextBuffers.</summary>
        public const string AttributeKey = "interactiveRanges";

        private readonly List<IInteractiveRangeProvider> providers = new(4);

        /// <summary>Gets the number of registered providers.</summary>
        public int ProviderCount => providers.Count;

        /// <summary>
        /// Registers a provider with the registry.
        /// </summary>
        /// <param name="provider">The provider to register.</param>
        public void Register(IInteractiveRangeProvider provider)
        {
            if (provider == null || providers.Contains(provider))
                return;

            providers.Add(provider);
        }

        /// <summary>
        /// Unregisters a provider from the registry.
        /// </summary>
        /// <param name="provider">The provider to unregister.</param>
        public void Unregister(IInteractiveRangeProvider provider)
        {
            if (provider != null)
                providers.Remove(provider);
        }

        /// <summary>
        /// Attempts to find an interactive range at the specified cluster position.
        /// </summary>
        /// <param name="cluster">The cluster index to check.</param>
        /// <param name="range">The found range, or default if none.</param>
        /// <param name="provider">The provider that owns the range, or null if none.</param>
        /// <returns>True if a range was found at the cluster position.</returns>
        /// <remarks>
        /// When multiple ranges overlap, the one with highest priority wins.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetRangeAt(int cluster, out InteractiveRange range, out IInteractiveRangeProvider provider)
        {
            range = default;
            provider = null;

            var count = providers.Count;
            if (count == 0)
                return false;

            var bestPriority = int.MinValue;
            var found = false;

            for (var i = 0; i < count; i++)
            {
                var p = providers[i];
                if (p.TryGetRange(cluster, out var candidate) && candidate.priority > bestPriority)
                {
                    range = candidate;
                    provider = p;
                    bestPriority = candidate.priority;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Attempts to find an interactive range at the specified cluster position.
        /// </summary>
        /// <param name="cluster">The cluster index to check.</param>
        /// <param name="range">The found range, or default if none.</param>
        /// <returns>True if a range was found at the cluster position.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetRangeAt(int cluster, out InteractiveRange range)
        {
            return TryGetRangeAt(cluster, out range, out _);
        }

        /// <inheritdoc/>
        public void Release()
        {
            providers.Clear();
        }

        /// <summary>
        /// Gets or creates the registry for the specified buffers.
        /// </summary>
        /// <param name="buffers">The UniText buffers.</param>
        /// <returns>The interactive range registry.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InteractiveRangeRegistry GetOrCreate(UniTextBuffers buffers)
        {
            return buffers?.GetOrCreateAttributeData<InteractiveRangeRegistry>(AttributeKey);
        }

        /// <summary>
        /// Gets the registry for the specified buffers if it exists.
        /// </summary>
        /// <param name="buffers">The UniText buffers.</param>
        /// <returns>The interactive range registry, or null if not created.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InteractiveRangeRegistry Get(UniTextBuffers buffers)
        {
            return buffers?.GetAttributeData<InteractiveRangeRegistry>(AttributeKey);
        }
    }
}
