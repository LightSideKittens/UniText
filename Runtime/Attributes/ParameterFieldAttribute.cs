using System;

namespace LightSide
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ParameterFieldAttribute : Attribute
    {
        public int Order { get; }
        public string Name { get; }
        public string Type { get; }
        public string Default { get; }

        public ParameterFieldAttribute(int order, string name, string type, string defaultValue = "")
        {
            Order = order;
            Name = name;
            Type = type;
            Default = defaultValue;
        }
    }
}
