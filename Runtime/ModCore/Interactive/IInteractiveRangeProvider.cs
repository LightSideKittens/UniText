namespace LightSide
{
    /// <summary>
    /// Interface for modifiers that provide interactive text ranges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implement this interface to register clickable/hoverable regions in text.
    /// The <see cref="InteractiveRangeRegistry"/> queries all providers to find
    /// which range (if any) contains a given cluster position.
    /// </para>
    /// <para>
    /// For modifiers that also want to handle click/hover events directly,
    /// implement <see cref="IInteractiveRangeHandler"/> instead.
    /// </para>
    /// </remarks>
    /// <seealso cref="IInteractiveRangeHandler"/>
    /// <seealso cref="InteractiveRangeRegistry"/>
    public interface IInteractiveRangeProvider
    {
        /// <summary>Identifies the type of ranges this provider creates (e.g., "link", "hashtag").</summary>
        string RangeType { get; }

        /// <summary>Priority for resolving overlapping ranges. Higher values win.</summary>
        int Priority { get; }

        /// <summary>
        /// Attempts to find an interactive range containing the specified cluster.
        /// </summary>
        /// <param name="cluster">The cluster index to check.</param>
        /// <param name="range">The range if found, otherwise default.</param>
        /// <returns>True if a range was found at the cluster position.</returns>
        bool TryGetRange(int cluster, out InteractiveRange range);
    }
}
