using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    [Serializable]
    public class TypedList<T> : IReadOnlyList<T>
    {
        [SerializeReference, TypeSelector]
        public List<T> items = new();

        public TypedList() { }
        public TypedList(params T[] initialItems) => items = new List<T>(initialItems);

        public int Count => items.Count;

        public T this[int index]
        {
            get => items[index];
            set => items[index] = value;
        }

        public void Add(T item) => items.Add(item);
        public bool Remove(T item) => items.Remove(item);
        public void RemoveAt(int index) => items.RemoveAt(index);
        public void Clear() => items.Clear();
        public void Insert(int index, T item) => items.Insert(index, item);
        public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
    }

    [Serializable]
    public class StyledList<T> : IReadOnlyList<T>
    {
        public List<T> items = new();

        public StyledList() { }
        public StyledList(params T[] initialItems) => items = new List<T>(initialItems);

        public int Count => items.Count;
        public int Length => items.Count;

        public T this[int index]
        {
            get => items[index];
            set => items[index] = value;
        }

        public void Add(T item) => items.Add(item);
        public bool Remove(T item) => items.Remove(item);
        public void RemoveAt(int index) => items.RemoveAt(index);
        public void Clear() => items.Clear();
        public void Insert(int index, T item) => items.Insert(index, item);
        public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
        public T[] ToArray() => items.ToArray();
    }

}
