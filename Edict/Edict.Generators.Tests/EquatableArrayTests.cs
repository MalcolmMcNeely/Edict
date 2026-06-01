using System;
using System.Linq;

using Xunit;

namespace Edict.Generators.Tests;

// EquatableArray exists so a record field of arrays keeps value-equality, which
// is what Roslyn's incremental cache keys on. These assert the equality and hash
// contract that promise rests on — get it wrong and the generator silently
// rebuilds every downstream step on unrelated edits.
public class EquatableArrayTests
{
    [Fact]
    public void Equals_SameElementsInSameOrder_IsTrue()
    {
        var left = new EquatableArray<string>(new[] { "a", "b", "c" });
        var right = new EquatableArray<string>(new[] { "a", "b", "c" });

        Assert.True(left.Equals(right));
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equals_DistinctBackingArraysWithSameContent_IsTrue()
    {
        // The whole point: structurally identical, reference-distinct arrays
        // (what a re-run transform produces) must still compare equal.
        var first = new[] { "x", "y" };
        var second = new[] { "x", "y" };
        Assert.NotSame(first, second);

        Assert.True(new EquatableArray<string>(first).Equals(new EquatableArray<string>(second)));
    }

    [Fact]
    public void Equals_DifferentLengths_IsFalse()
    {
        var shorter = new EquatableArray<int>(new[] { 1, 2 });
        var longer = new EquatableArray<int>(new[] { 1, 2, 3 });

        Assert.False(shorter.Equals(longer));
        Assert.True(shorter != longer);
    }

    [Fact]
    public void Equals_SameLengthDifferentElements_IsFalse()
    {
        var left = new EquatableArray<int>(new[] { 1, 2, 3 });
        var right = new EquatableArray<int>(new[] { 1, 9, 3 });

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_SameElementsDifferentOrder_IsFalse()
    {
        var left = new EquatableArray<string>(new[] { "a", "b" });
        var right = new EquatableArray<string>(new[] { "b", "a" });

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_DefaultAndEmpty_AreEqualAndHashAlike()
    {
        // default(EquatableArray) has a null backing array; Empty has a
        // zero-length one. Equals treats them as the same value, so GetHashCode
        // must agree — otherwise they are equal with different hash codes and a
        // hash-keyed cache misbehaves.
        var fromDefault = default(EquatableArray<string>);
        var fromEmpty = EquatableArray<string>.Empty;
        var fromEmptyArray = new EquatableArray<string>(Array.Empty<string>());

        Assert.True(fromDefault.Equals(fromEmpty));
        Assert.True(fromDefault.Equals(fromEmptyArray));
        Assert.True(fromEmpty.Equals(fromDefault));
        Assert.Equal(fromDefault.GetHashCode(), fromEmpty.GetHashCode());
        Assert.Equal(fromDefault.GetHashCode(), fromEmptyArray.GetHashCode());
    }

    [Fact]
    public void Equals_NonEmptyAndEmpty_IsFalse()
    {
        var populated = new EquatableArray<int>(new[] { 1 });

        Assert.False(populated.Equals(EquatableArray<int>.Empty));
        Assert.False(populated.Equals(default));
    }

    [Fact]
    public void Equals_BoxedOther_RoutesThroughTypedEquals()
    {
        var left = new EquatableArray<int>(new[] { 1, 2 });
        object boxedEqual = new EquatableArray<int>(new[] { 1, 2 });
        object boxedDifferentType = "not an array";

        Assert.True(left.Equals(boxedEqual));
        Assert.False(left.Equals(boxedDifferentType));
    }

    [Fact]
    public void LengthAndIsEmpty_ReflectBackingArray()
    {
        Assert.Equal(0, default(EquatableArray<int>).Length);
        Assert.True(default(EquatableArray<int>).IsEmpty);
        Assert.True(EquatableArray<int>.Empty.IsEmpty);

        var populated = new EquatableArray<int>(new[] { 1, 2, 3 });
        Assert.Equal(3, populated.Length);
        Assert.False(populated.IsEmpty);
    }

    [Fact]
    public void Indexer_ReturnsElementAtPosition()
    {
        var array = new EquatableArray<string>(new[] { "first", "second" });

        Assert.Equal("first", array[0]);
        Assert.Equal("second", array[1]);
    }

    [Fact]
    public void Enumeration_YieldsElements_AndDefaultYieldsNothing()
    {
        var populated = new EquatableArray<int>(new[] { 1, 2, 3 });

        Assert.Equal(new[] { 1, 2, 3 }, populated.ToArray());
        Assert.Empty(default(EquatableArray<int>).ToArray());
    }

    [Fact]
    public void Constructor_FromEnumerable_MaterialisesOnce()
    {
        var array = new EquatableArray<int>(Enumerable.Range(1, 3));

        Assert.Equal(3, array.Length);
        Assert.Equal(new[] { 1, 2, 3 }, array.ToArray());
    }
}
