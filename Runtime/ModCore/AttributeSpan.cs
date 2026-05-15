using System;


namespace LightSide
{
    internal struct AttributeSpan : IEquatable<AttributeSpan>
    {
        public int start;


        public int end;


        public readonly BaseModifier modifier;


        public readonly string parameter;

        public int Length => end - start;

        public AttributeSpan(int start, int end, BaseModifier modifier, string parameter = null)
        {
            this.start = start;
            this.end = end;
            this.modifier = modifier;
            this.parameter = parameter;
        }

        public bool Contains(int index)
        {
            return index >= start && index < end;
        }

        public bool Overlaps(AttributeSpan other)
        {
            return start < other.end && end > other.start;
        }

        public bool Equals(AttributeSpan other)
        {
            return start == other.start && end == other.end && ReferenceEquals(modifier, other.modifier);
        }

        public override bool Equals(object obj)
        {
            return obj is AttributeSpan other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(start, end, modifier);
        }

        public static bool operator ==(AttributeSpan left, AttributeSpan right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AttributeSpan left, AttributeSpan right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"[{start}-{end}] {modifier?.GetType().Name ?? "null"}";
        }
    }
}
