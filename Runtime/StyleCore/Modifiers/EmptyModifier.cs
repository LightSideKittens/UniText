using System;

namespace LightSide
{
    /// <summary>
    /// A no-op modifier that performs no action.
    /// </summary>
    /// <remarks>
    /// Used as a placeholder or for tags that should be parsed but have no visual effect.
    /// Can be associated with custom parse rules to extract data without modifying rendering.
    /// </remarks>
    [Serializable]
    [TypeDescription("An empty modifier with no effect. Use as a starting point for custom logic.")]
    public class EmptyModifier : BaseModifier
    {
        protected override void OnApply(int start, int end, string parameter) { }
    }
}
