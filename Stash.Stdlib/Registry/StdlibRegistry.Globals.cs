namespace Stash.Stdlib;

using System.Collections.Generic;
using Stash.Stdlib.Models;

public static partial class StdlibRegistry
{
    // ── Built-in Global Functions ──

    public static readonly IReadOnlyList<BuiltInFunction> Functions = new[]
    {
        // Introspection and utilities
        new BuiltInFunction("typeof", new[] { new BuiltInParam("value") }, "string",
            Documentation: "Returns the type name of a value as a string.\n@param value The value to inspect\n@return The type name: \"int\", \"float\", \"string\", \"bool\", \"null\", \"array\", \"dict\", \"struct\", \"enum\", \"function\", \"namespace\", \"range\", or \"Error\""),
        new BuiltInFunction("len", new[] { new BuiltInParam("value") }, "int",
            Documentation: "Returns the length of a string, array, or dictionary.\n@param value A string, array, or dictionary\n@return The number of characters, elements, or entries"),
        new BuiltInFunction("lastError", System.Array.Empty<BuiltInParam>(), "Error",
            Documentation: "Returns the last error value as an Error object, or null if no error has occurred.\n@return An Error object with .message, .type, and .stack fields, or null"),
        // Utility functions
        new BuiltInFunction("range", new[] { new BuiltInParam("start_or_end", "int"), new BuiltInParam("end", "int"), new BuiltInParam("step", "int") }, "array",
            Documentation: "Creates an array of integers in sequence. With one argument, generates 0 to end (exclusive). With two, generates start to end (exclusive). With three, uses the given step.\n@param start_or_end The end value (if one arg) or start value (if two/three args)\n@param end The end value (exclusive)\n@param step The increment between values\n@return An array of integers"),
        new BuiltInFunction("exit", new[] { new BuiltInParam("code", "int") },
            Documentation: "Terminates the program immediately with the specified exit code.\n@param code The exit code to return to the operating system"),
        new BuiltInFunction("hash", new[] { new BuiltInParam("value") }, "int",
            Documentation: "Returns a hash code for the given value.\n@param value The value to hash\n@return An integer hash code"),
        new BuiltInFunction("nameof", new[] { new BuiltInParam("value") }, "string",
            Documentation: "Returns the name of a variable, function, or type as a string.\n@param value The symbol to get the name of\n@return The name as a string"),
    };
}
