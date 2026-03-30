using System;
using UnityEngine;

namespace LightSide
{
    [AttributeUsage(AttributeTargets.Field)]
    public class TypeSelectorAttribute : PropertyAttribute { }

    /// <summary>Hides a type from the TypeSelector dropdown while keeping it deserializable.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class HideFromTypeSelectorAttribute : Attribute { }
}
