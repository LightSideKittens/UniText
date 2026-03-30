using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Marks a string field as a default parameter for a parse rule.
    /// Draws a toggle + rich parameter fields based on the paired modifier's ParameterFieldAttribute.
    /// </summary>
    public sealed class DefaultParameterAttribute : PropertyAttribute { }
}
