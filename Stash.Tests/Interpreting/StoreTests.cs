using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;

namespace Stash.Tests.Interpreting;

public class StoreTests
{
    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    // ===== store.set / store.get =====

    [Fact]
    public void Store_SetAndGetString()
    {
        var result = Run("""
            store.clear();
            store.set("name", "Alice");
            let result = store.get("name");
            """);
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void Store_SetAndGetInteger()
    {
        var result = Run("""
            store.clear();
            store.set("count", 42);
            let result = store.get("count");
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Store_SetAndGetBoolean()
    {
        var result = Run("""
            store.clear();
            store.set("flag", true);
            let result = store.get("flag");
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Store_SetNullValue()
    {
        var result = Run("""
            store.clear();
            store.set("key", null);
            let result = store.get("key");
            """);
        Assert.Null(result);
    }

    [Fact]
    public void Store_SetOverwritesExistingValue()
    {
        var result = Run("""
            store.clear();
            store.set("x", "first");
            store.set("x", "second");
            let result = store.get("x");
            """);
        Assert.Equal("second", result);
    }

    [Fact]
    public void Store_GetReturnsNullForMissingKey()
    {
        var result = Run("""
            store.clear();
            let result = store.get("nonexistent");
            """);
        Assert.Null(result);
    }

    [Fact]
    public void Store_SetWithNonStringKeyThrows()
    {
        RunExpectingError("""
            store.clear();
            store.set(123, "value");
            """);
    }

    [Fact]
    public void Store_GetWithNonStringKeyThrows()
    {
        RunExpectingError("""
            store.clear();
            store.get(123);
            """);
    }

    // ===== store.has =====

    [Fact]
    public void Store_HasReturnsTrueForExistingKey()
    {
        var result = Run("""
            store.clear();
            store.set("present", "yes");
            let result = store.has("present");
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Store_HasReturnsFalseForMissingKey()
    {
        var result = Run("""
            store.clear();
            let result = store.has("absent");
            """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Store_HasWithNonStringKeyThrows()
    {
        RunExpectingError("""
            store.clear();
            store.has(99);
            """);
    }

    // ===== store.remove =====

    [Fact]
    public void Store_RemoveExistingKeyReturnsTrue()
    {
        var result = Run("""
            store.clear();
            store.set("item", "value");
            let result = store.remove("item");
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Store_RemoveNonExistentKeyReturnsFalse()
    {
        var result = Run("""
            store.clear();
            let result = store.remove("ghost");
            """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Store_AfterRemoveHasReturnsFalse()
    {
        var result = Run("""
            store.clear();
            store.set("temp", "data");
            store.remove("temp");
            let result = store.has("temp");
            """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Store_RemoveWithNonStringKeyThrows()
    {
        RunExpectingError("""
            store.clear();
            store.remove(true);
            """);
    }

    // ===== store.keys =====

    [Fact]
    public void Store_KeysReturnsEmptyArrayForEmptyStore()
    {
        var result = Run("""
            store.clear();
            let result = store.keys();
            """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void Store_KeysReturnsAllKeys()
    {
        var result = Run("""
            store.clear();
            store.set("a", 1);
            store.set("b", 2);
            store.set("c", 3);
            let result = store.keys();
            """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Contains("a", list);
        Assert.Contains("b", list);
        Assert.Contains("c", list);
    }

    // ===== store.values =====

    [Fact]
    public void Store_ValuesReturnsEmptyArrayForEmptyStore()
    {
        var result = Run("""
            store.clear();
            let result = store.values();
            """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void Store_ValuesReturnsAllValues()
    {
        var result = Run("""
            store.clear();
            store.set("x", 10);
            store.set("y", 20);
            let result = store.values();
            """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Contains(10L, list);
        Assert.Contains(20L, list);
    }

    // ===== store.clear =====

    [Fact]
    public void Store_ClearResetsSizeToZero()
    {
        var result = Run("""
            store.clear();
            store.set("a", 1);
            store.set("b", 2);
            store.clear();
            let result = store.size();
            """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Store_ClearRemovesPreviouslySetValues()
    {
        var result = Run("""
            store.clear();
            store.set("key", "value");
            store.clear();
            let result = store.has("key");
            """);
        Assert.Equal(false, result);
    }

    // ===== store.size =====

    [Fact]
    public void Store_SizeReturnsZeroForEmptyStore()
    {
        var result = Run("""
            store.clear();
            let result = store.size();
            """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Store_SizeReturnsCorrectCountAfterAddingEntries()
    {
        var result = Run("""
            store.clear();
            store.set("one", 1);
            store.set("two", 2);
            store.set("three", 3);
            let result = store.size();
            """);
        Assert.Equal(3L, result);
    }

    // ===== store.all =====

    [Fact]
    public void Store_AllReturnsEmptyDictForEmptyStore()
    {
        var result = Run("""
            store.clear();
            let d = store.all();
            let result = dict.size(d);
            """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Store_AllReturnsDictWithAllEntries()
    {
        var result = Run("""
            store.clear();
            store.set("color", "blue");
            store.set("size", 42);
            let d = store.all();
            let result = dict.get(d, "color");
            """);
        Assert.Equal("blue", result);
    }

    // ===== store.scope =====

    [Fact]
    public void Store_ScopeReturnsMatchingPrefixEntries()
    {
        var result = Run("""
            store.clear();
            store.set("app.name", "Stash");
            store.set("app.version", "1.0");
            store.set("db.host", "localhost");
            let d = store.scope("app.");
            let result = dict.get(d, "app.name");
            """);
        Assert.Equal("Stash", result);
    }

    [Fact]
    public void Store_ScopeExcludesNonMatchingEntries()
    {
        var result = Run("""
            store.clear();
            store.set("app.name", "Stash");
            store.set("db.host", "localhost");
            let d = store.scope("app.");
            let result = dict.size(d);
            """);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Store_ScopeReturnsEmptyDictWhenNoEntriesMatch()
    {
        var result = Run("""
            store.clear();
            store.set("foo.bar", "baz");
            let d = store.scope("xyz.");
            let result = dict.size(d);
            """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Store_ScopeWithNonStringPrefixThrows()
    {
        RunExpectingError("""
            store.clear();
            store.scope(42);
            """);
    }
}
