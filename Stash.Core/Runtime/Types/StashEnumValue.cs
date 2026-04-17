namespace Stash.Runtime.Types;

using Stash.Common;
using Stash.Runtime.Protocols;

/// <summary>
/// Represents a specific enum member value — identified by its type name and member name.
/// Equality is by identity: same type name AND same member name.
/// </summary>
public class StashEnumValue : IVMTyped, IVMFieldAccessible, IVMEquatable, IVMStringifiable
{
    public string TypeName { get; }
    public string MemberName { get; }

    public StashEnumValue(string typeName, string memberName)
    {
        TypeName = typeName;
        MemberName = memberName;
    }

    public override bool Equals(object? obj)
    {
        if (obj is StashEnumValue other)
        {
            return TypeName == other.TypeName && MemberName == other.MemberName;
        }
        return false;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (TypeName?.GetHashCode() ?? 0);
            hash = hash * 31 + (MemberName?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public override string ToString() => $"{TypeName}.{MemberName}";

    // --- Protocol implementations ---

    public string VMTypeName => TypeName;

    public bool VMTryGetField(string name, out StashValue value, SourceSpan? span)
    {
        switch (name)
        {
            case "typeName":
                value = StashValue.FromObj(TypeName);
                return true;
            case "memberName":
                value = StashValue.FromObj(MemberName);
                return true;
            default:
                value = default;
                return false;
        }
    }

    public bool VMEquals(StashValue other)
    {
        if (other.IsObj && other.AsObj is StashEnumValue otherEv)
            return TypeName == otherEv.TypeName && MemberName == otherEv.MemberName;
        return false;
    }

    public string VMToString() => ToString();
}
