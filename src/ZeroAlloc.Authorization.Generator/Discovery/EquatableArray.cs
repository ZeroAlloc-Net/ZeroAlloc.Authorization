using System.Collections;
using System.Collections.Generic;

namespace ZeroAlloc.Authorization.Generator.Discovery;

/// <summary>
/// An immutable array that compares by its elements, so a pipeline model holding one compares
/// equal across runs when its contents do. <see cref="System.Collections.Immutable.ImmutableArray{T}"/>
/// compares by reference, which would make every model look changed on every edit.
/// </summary>
internal readonly struct EquatableArray<T> : System.IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly T[]? _items;

    public EquatableArray(T[] items) => _items = items;

    public static EquatableArray<T> Empty => default;

    public int Count => _items?.Length ?? 0;

    public T this[int index] => _items![index];

    public bool Equals(EquatableArray<T> other)
    {
        var count = Count;
        if (count != other.Count) return false;
        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < count; i++)
        {
            if (!comparer.Equals(_items![i], other._items![i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            var comparer = EqualityComparer<T>.Default;
            for (var i = 0; i < Count; i++)
            {
                hash = (hash * 31) + (_items![i] is { } item ? comparer.GetHashCode(item) : 0);
            }
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? System.Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
