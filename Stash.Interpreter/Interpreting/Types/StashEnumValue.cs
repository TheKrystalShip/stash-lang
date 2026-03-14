namespace Stash.Interpreting.Types;

/// <summary>
/// Represents a specific enum member value — identified by its type name and member name.
/// Equality is by identity: same type name AND same member name.
/// </summary>
public class StashEnumValue
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
}
