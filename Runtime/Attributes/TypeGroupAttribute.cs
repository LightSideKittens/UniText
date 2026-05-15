using System;

namespace LightSide
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class TypeGroupAttribute : Attribute
    {
        public string GroupName { get; }
        public int Order { get; }

        public TypeGroupAttribute(string groupName, int order = 0)
        {
            GroupName = groupName;
            Order = order;
        }
    }

}
