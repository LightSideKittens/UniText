using System;

namespace LightSide
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class TypeDescriptionAttribute : Attribute
    {
        public string Description { get; }

        public TypeDescriptionAttribute(string description)
        {
            Description = description;
        }
    }
}
