namespace Stash.Stdlib.Generators;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

internal sealed record NamespaceModel(
    string ClassName,
    string ClassFullName,
    string ContainingNamespace,
    string StashName,
    string CapabilityFullName,
    EquatableArray<FunctionModel> Functions,
    EquatableArray<ConstantModel> Constants,
    EquatableArray<StructModel> Structs,
    EquatableArray<EnumModel> Enums);

internal sealed record FunctionModel(
    string MethodName,
    string StashName,
    bool Raw,
    string ReturnTypeFullName,
    string ReturnTypeStash,
    bool TakesContext,
    bool IsVariadic,
    EquatableArray<ParameterModel> Parameters,
    string? Documentation,
    string? DeprecationReplacement,
    string CapabilityFullName = "global::Stash.Runtime.StashCapabilities.None");

internal sealed record ParameterModel(
    string CSharpName,
    string StashName,
    string CSharpTypeFullName,
    string StashTypeLabel,
    string ExtractorExpression,
    bool HasDefaultValue,
    string? DefaultValueLiteral,
    bool IsNullable,
    bool IsParamsArray);

internal sealed record ConstantModel(
    string FieldName,
    string StashName,
    string CSharpTypeFullName,
    string StashTypeLabel,
    string DisplayValue,
    string? Documentation,
    string? DeprecationReplacement);

internal sealed record StructModel(
    string TypeFullName,
    string StashName,
    EquatableArray<FieldModel> Fields,
    string? Documentation = null);

internal sealed record FieldModel(
    string CSharpName,
    string StashName,
    string StashTypeLabel);

internal sealed record EnumModel(
    string TypeFullName,
    string StashName,
    EquatableArray<string> Members,
    string? Documentation = null);

/// <summary>
/// Equatable wrapper around <see cref="ImmutableArray{T}"/> for use as a generator pipeline value.
/// Roslyn's incremental cache compares values by <c>Equals</c>; <see cref="ImmutableArray{T}"/>
/// uses reference equality, which defeats caching.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly T[] _items;

    public EquatableArray(T[] items)
    {
        _items = items ?? Array.Empty<T>();
    }

    public static EquatableArray<T> Empty => new(Array.Empty<T>());

    public int Count => _items.Length;
    public T this[int index] => _items[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_items.Length != other._items.Length) return false;
        for (int i = 0; i < _items.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(_items[i], other._items[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (var item in _items)
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

internal static class EquatableArrayExtensions
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> source)
        => new(source.ToArray());
}
