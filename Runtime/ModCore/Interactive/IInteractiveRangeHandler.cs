namespace LightSide
{
    /// <summary>
    /// Interface for modifiers that provide interactive ranges AND handle their own interactions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Extends <see cref="IInteractiveRangeProvider"/> with callbacks for click and hover events.
    /// Implement this when the modifier needs to respond directly to user interactions
    /// (e.g., LinkModifier opening URLs, custom modifiers showing tooltips).
    /// </para>
    /// <para>
    /// For simple providers that only define ranges but don't handle events,
    /// implement <see cref="IInteractiveRangeProvider"/> instead.
    /// </para>
    /// </remarks>
    /// <seealso cref="IInteractiveRangeProvider"/>
    /// <seealso cref="InteractiveModifier"/>
    public interface IInteractiveRangeHandler : IInteractiveRangeProvider
    {
        /// <summary>Called when the range is clicked/tapped.</summary>
        /// <param name="range">The range that was clicked.</param>
        /// <param name="hit">The text hit result with position details.</param>
        void OnRangeClicked(InteractiveRange range, TextHitResult hit);

        /// <summary>Called when the pointer enters the range (desktop only).</summary>
        /// <param name="range">The range being entered.</param>
        /// <param name="hit">The text hit result with position details.</param>
        void OnRangeEntered(InteractiveRange range, TextHitResult hit);

        /// <summary>Called when the pointer exits the range (desktop only).</summary>
        /// <param name="range">The range being exited.</param>
        void OnRangeExited(InteractiveRange range);
    }
}
