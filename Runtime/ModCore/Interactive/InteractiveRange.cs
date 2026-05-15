namespace LightSide
{
    /// <summary>
    /// Represents a clickable/hoverable range within text.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="IInteractiveRangeProvider"/> implementations to define
    /// interactive regions like links, hashtags, mentions, or custom clickable areas.
    /// </remarks>
    public readonly struct InteractiveRange
    {
        /// <summary>Start cluster index (inclusive).</summary>
        public readonly int start;

        /// <summary>End cluster index (exclusive).</summary>
        public readonly int end;

        /// <summary>Type identifier (e.g., "link", "hashtag", "mention").</summary>
        public readonly string type;

        /// <summary>Associated data (URL, tag text, user ID, etc.).</summary>
        public readonly string data;

        /// <summary>Priority for resolving overlapping ranges. Higher wins.</summary>
        public readonly int priority;

        /// <summary>Creates an interactive range.</summary>
        /// <param name="start">Start cluster index (inclusive).</param>
        /// <param name="end">End cluster index (exclusive).</param>
        /// <param name="type">Type identifier.</param>
        /// <param name="data">Associated data.</param>
        /// <param name="priority">Priority for overlap resolution.</param>
        public InteractiveRange(int start, int end, string type, string data, int priority = 0)
        {
            this.start = start;
            this.end = end;
            this.type = type;
            this.data = data;
            this.priority = priority;
        }

        /// <summary>Returns true if the cluster is within this range.</summary>
        public bool Contains(int cluster) => cluster >= start && cluster < end;

        /// <summary>Returns an empty/invalid range.</summary>
        public static InteractiveRange None => default;

        /// <summary>Returns true if this is a valid range (has non-null type).</summary>
        public bool IsValid => type != null;
    }
}
