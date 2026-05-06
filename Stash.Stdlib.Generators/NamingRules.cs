namespace Stash.Stdlib.Generators;

internal static class NamingRules
{
    public static string ToCamelCase(string pascal)
    {
        if (string.IsNullOrEmpty(pascal)) return pascal;
        if (char.IsLower(pascal[0])) return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
    }

    public static bool HasConsecutiveUppercase(string camelCased)
    {
        if (string.IsNullOrEmpty(camelCased)) return false;
        for (int i = 0; i + 1 < camelCased.Length; i++)
        {
            if (char.IsUpper(camelCased[i]) && char.IsUpper(camelCased[i + 1]))
                return true;
        }
        return false;
    }

    public static string NamespaceFromClass(string className)
    {
        const string suffix = "BuiltIns";
        string trimmed = className.EndsWith(suffix) && className.Length > suffix.Length
            ? className.Substring(0, className.Length - suffix.Length)
            : className;
        return trimmed.ToLowerInvariant();
    }
}
