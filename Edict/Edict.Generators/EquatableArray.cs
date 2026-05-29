using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Edict.Generators;

/// <summary>
/// A value-equal wrapper around a backing <see cref="T:T[]"/> for use as a
/// field on incremental-generator model records.
/// <para>
/// <see cref="System.Collections.Immutable.ImmutableArray{T}"/>'s built-in
/// equality is reference-only on its underlying array, so a record containing
/// an <c>ImmutableArray</c> field silently breaks Roslyn's value-equality
/// cache key: a transform that re-runs (e.g. because the compilation changed
/// under an unrelated edit) produces a structurally-identical but
/// reference-distinct array, the record's generated <c>Equals</c> returns
/// false, and every downstream incremental step rebuilds. This wrapper
/// compares element-wise instead, so the record's <c>Equals</c> stays honest.
/// </para>
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(Array.Empty<T>());

    readonly T[]? array;

    public EquatableArray(T[] array)
    {
        this.array = array;
    }

    public EquatableArray(IEnumerable<T> items)
    {
        array = items.ToArray();
    }

    public T this[int index] => array![index];

    public int Length => array?.Length ?? 0;

    public bool IsEmpty => Length == 0;

    public bool Equals(EquatableArray<T> other)
    {
        if (array is null)
        {
            return other.array is null || other.array.Length == 0;
        }

        if (other.array is null)
        {
            return array.Length == 0;
        }

        if (array.Length != other.array.Length)
        {
            return false;
        }

        for (var i = 0; i < array.Length; i++)
        {
            if (!array[i].Equals(other.array[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (array is null)
        {
            return 0;
        }

        unchecked
        {
            var hash = 17;
            foreach (var item in array)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        if (array is null)
        {
            yield break;
        }

        foreach (var item in array)
        {
            yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) =>
        left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) =>
        !left.Equals(right);
}
