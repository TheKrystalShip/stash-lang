using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class ExtendBlockTests
{
    private static object? Run(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);

        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
    }

    private static void RunExpectingParseError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();
        Assert.NotEmpty(parser.Errors);
    }

    // ── 1. Extending Built-in Type: string ────────────────────────────────────

    [Fact]
    public void ExtendString_BasicMethod_ReturnsSelfTransformed()
    {
        var result = Run("""
            extend string {
                fn shout() {
                    return self.upper() + "!!!";
                }
            }
            let result = "hello".shout();
            """);
        Assert.Equal("HELLO!!!", result);
    }

    [Fact]
    public void ExtendString_MethodWithArgs_PassesArgsCorrectly()
    {
        var result = Run("""
            extend string {
                fn wrap(prefix, suffix) {
                    return prefix + self + suffix;
                }
            }
            let result = "world".wrap("[", "]");
            """);
        Assert.Equal("[world]", result);
    }

    [Fact]
    public void ExtendString_SelfReassignment_ThrowsRuntimeError()
    {
        RunExpectingError("""
            extend string {
                fn mutate() {
                    self = "changed";
                }
            }
            let s = "original";
            s.mutate();
            let result = s;
            """);
    }

    [Fact]
    public void ExtendString_MethodChainsExtensionThenUfcs_Works()
    {
        var result = Run("""
            extend string {
                fn exclaim() {
                    return self + "!";
                }
            }
            let result = "hello".exclaim().upper();
            """);
        Assert.Equal("HELLO!", result);
    }

    [Fact]
    public void ExtendString_MethodChainsUfcsThenExtension_Works()
    {
        var result = Run("""
            extend string {
                fn exclaim() {
                    return self + "!";
                }
            }
            let result = "hello".upper().exclaim();
            """);
        Assert.Equal("HELLO!", result);
    }

    [Fact]
    public void ExtendString_MultipleExtendBlocks_Accumulate()
    {
        var result = Run("""
            extend string {
                fn shout() { return self.upper() + "!"; }
            }
            extend string {
                fn whisper() { return self.lower() + "..."; }
            }
            let s = "Hello";
            let result = s.shout() + " " + s.whisper();
            """);
        Assert.Equal("HELLO! hello...", result);
    }

    [Fact]
    public void ExtendString_ShadowsUfcs_ExtensionWins()
    {
        var result = Run("""
            extend string {
                fn upper() { return "overridden"; }
            }
            let result = "hello".upper();
            """);
        Assert.Equal("overridden", result);
    }

    [Fact]
    public void ExtendString_EmptyString_SelfIsEmpty()
    {
        var result = Run("""
            extend string {
                fn isEmpty() { return self == ""; }
            }
            let result = "".isEmpty();
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ExtendString_StringLiteralDirect_Works()
    {
        var result = Run("""
            extend string {
                fn greet() { return "Hello, " + self + "!"; }
            }
            let result = "World".greet();
            """);
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void ExtendString_SelfAccessesStringMethods_ViaUfcs()
    {
        var result = Run("""
            extend string {
                fn titleShout() { return self.title().upper(); }
            }
            let result = "hello world".titleShout();
            """);
        Assert.Equal("HELLO WORLD", result);
    }

    // ── 2. Extending Built-in Type: array ─────────────────────────────────────

    [Fact]
    public void ExtendArray_BasicMethod_ReturnsDerivedValue()
    {
        var result = Run("""
            extend array {
                fn second() { return self[1]; }
            }
            let result = [10, 20, 30].second();
            """);
        Assert.Equal(20L, result);
    }

    [Fact]
    public void ExtendArray_MethodWithCallback_Works()
    {
        var result = Run("""
            extend array {
                fn myFilter(pred) { return self.filter(pred); }
            }
            let result = [1, 2, 3, 4, 5].myFilter((x) => x > 3);
            """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(4L, list[0]);
        Assert.Equal(5L, list[1]);
    }

    [Fact]
    public void ExtendArray_SelfIsArray_CanIndex()
    {
        var result = Run("""
            extend array {
                fn head() { return self[0]; }
            }
            let result = ["a", "b", "c"].head();
            """);
        Assert.Equal("a", result);
    }

    [Fact]
    public void ExtendArray_ChainsWithUfcs_Works()
    {
        var result = Run("""
            extend array {
                fn myFirst() { return self[0]; }
            }
            let a = [3, 1, 2];
            a.sort();
            let result = a.myFirst();
            """);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void ExtendArray_EmptyArray_Works()
    {
        var result = Run("""
            extend array {
                fn myCount() {
                    let n = 0;
                    for (let item in self) { n = n + 1; }
                    return n;
                }
            }
            let result = [].myCount();
            """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void ExtendArray_MultipleExtensions_Accumulate()
    {
        var result = Run("""
            extend array {
                fn myFirst() { return self[0]; }
            }
            extend array {
                fn myLast() { return self[len(self) - 1]; }
            }
            let a = [10, 20, 30];
            let result = a.myFirst() + a.myLast();
            """);
        Assert.Equal(40L, result);
    }

    [Fact]
    public void ExtendArray_ReturnsModifiedArray_EnablesChaining()
    {
        var result = Run("""
            extend array {
                fn withItem(item) {
                    arr.push(self, item);
                    return self;
                }
            }
            let a = [1, 2];
            let result = a.withItem(3).join(", ");
            """);
        Assert.Equal("1, 2, 3", result);
    }

    [Fact]
    public void ExtendArray_NestedArrays_Works()
    {
        var result = Run("""
            extend array {
                fn flatFirst() { return self[0][0]; }
            }
            let result = [[10, 20], [30, 40]].flatFirst();
            """);
        Assert.Equal(10L, result);
    }

    // ── 3. Extending Built-in Type: int ───────────────────────────────────────

    [Fact]
    public void ExtendInt_BasicMethod_Works()
    {
        var result = Run("""
            extend int {
                fn isEven() { return self % 2 == 0; }
            }
            let result = 42.isEven();
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ExtendInt_MethodWithArgs_Works()
    {
        var result = Run("""
            extend int {
                fn clamp(lo, hi) {
                    if (self < lo) { return lo; }
                    if (self > hi) { return hi; }
                    return self;
                }
            }
            let result = 150.clamp(0, 100);
            """);
        Assert.Equal(100L, result);
    }

    [Fact]
    public void ExtendInt_NegativeNumber_Works()
    {
        var result = Run("""
            extend int {
                fn isPositive() { return self > 0; }
            }
            let n = -5;
            let result = n.isPositive();
            """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void ExtendInt_Zero_Works()
    {
        var result = Run("""
            extend int {
                fn isPositive() { return self > 0; }
            }
            let result = 0.isPositive();
            """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void ExtendInt_ArithmeticInExtension_Works()
    {
        var result = Run("""
            extend int {
                fn tripled() { return self * 3; }
            }
            let result = 7.tripled();
            """);
        Assert.Equal(21L, result);
    }

    // ── 4. Extending Built-in Type: float ─────────────────────────────────────

    [Fact]
    public void ExtendFloat_BasicMethod_Works()
    {
        var result = Run("""
            extend float {
                fn isPositive() { return self > 0.0; }
            }
            let result = 3.14.isPositive();
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ExtendFloat_SelfIsFloat_Works()
    {
        var result = Run("""
            extend float {
                fn doubled() { return self * 2.0; }
            }
            let result = 3.14.doubled();
            """);
        Assert.Equal(6.28, result);
    }

    [Fact]
    public void ExtendFloat_MethodWithArgs_Works()
    {
        var result = Run("""
            extend float {
                fn addTo(other) { return self + other; }
            }
            let result = 1.5.addTo(2.5);
            """);
        Assert.Equal(4.0, result);
    }

    // ── 5. Extending Built-in Type: dict ──────────────────────────────────────

    [Fact]
    public void ExtendDict_BasicMethod_Works()
    {
        // Extension method on dict is callable via dot notation
        var result = Run("""
            extend dict {
                fn hasKey(key) { return dict.has(self, key); }
            }
            let d = dict.new();
            dict.set(d, "x", 42);
            let result = d.hasKey("x");
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ExtendDict_ExtensionMethod_TakesPriorityOverKeyLookup()
    {
        // Extension method takes priority over dict key with the same name
        var result = Run("""
            extend dict {
                fn greeting() { return "hello from extension"; }
            }
            let d = dict.new();
            dict.set(d, "greeting", "stored_value");
            let result = d.greeting();
            """);
        Assert.Equal("hello from extension", result);
    }

    [Fact]
    public void ExtendDict_RegularDotAccessStillWorks_AfterExtend()
    {
        // Normal dict key access via dot notation is unaffected when no extension has the same name
        var result = Run("""
            extend dict {
                fn unused() { return null; }
            }
            let d = { name: "Alice", age: 30 };
            let result = d.name;
            """);
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void ExtendDict_ExtensionMethod_WithSelf_Works()
    {
        // Extension method on dict can access its own keys via self
        var result = Run("""
            extend dict {
                fn hasKey(key) { return dict.has(self, key); }
            }
            let d = dict.new();
            let result = d.hasKey("x");
            """);
        Assert.Equal(false, result);
    }

    // ── 6. Extending User-Defined Structs ─────────────────────────────────────

    [Fact]
    public void ExtendStruct_BasicMethod_Works()
    {
        var result = Run("""
            struct User { name, age }
            extend User {
                fn isAdult() { return self.age >= 18; }
            }
            let u = User { name: "Alice", age: 25 };
            let result = u.isAdult();
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ExtendStruct_AccessesFields_ViaSelf()
    {
        var result = Run("""
            struct User { name, age }
            extend User {
                fn summary() {
                    return self.name + " is " + self.age + " years old";
                }
            }
            let u = User { name: "Bob", age: 30 };
            let result = u.summary();
            """);
        Assert.Equal("Bob is 30 years old", result);
    }

    [Fact]
    public void ExtendStruct_MultipleMethodsInBlock_AllAvailable()
    {
        var result = Run("""
            struct Point { x, y }
            extend Point {
                fn sumCoords() { return self.x + self.y; }
                fn productCoords() { return self.x * self.y; }
            }
            let p = Point { x: 3, y: 4 };
            let result = p.sumCoords() + p.productCoords();
            """);
        Assert.Equal(19L, result);
    }

    [Fact]
    public void ExtendStruct_MultipleExtendBlocks_Accumulate()
    {
        var result = Run("""
            struct Point { x, y }
            extend Point {
                fn sumCoords() { return self.x + self.y; }
            }
            extend Point {
                fn diffCoords() { return self.x - self.y; }
            }
            let p = Point { x: 10, y: 3 };
            let result = p.sumCoords() + p.diffCoords();
            """);
        Assert.Equal(20L, result);
    }

    [Fact]
    public void ExtendStruct_MethodCallsOtherExtensionOnSelf()
    {
        var result = Run("""
            struct User { name }
            extend User {
                fn greeting() { return "Hello, " + self.name; }
                fn shout() { return self.greeting().upper(); }
            }
            let u = User { name: "Alice" };
            let result = u.shout();
            """);
        Assert.Equal("HELLO, ALICE", result);
    }

    [Fact]
    public void ExtendStruct_OriginalMethodCoexistsWithExtension()
    {
        var result = Run("""
            struct Animal {
                name,
                fn speak() { return self.name + " says hello"; }
            }
            extend Animal {
                fn shout() { return self.speak().upper(); }
            }
            let a = Animal { name: "Dog" };
            let result = a.shout();
            """);
        Assert.Equal("DOG SAYS HELLO", result);
    }

    [Fact]
    public void ExtendStruct_FieldBeforeExtension_FieldWins()
    {
        var result = Run("""
            struct Person { name }
            extend Person {
                fn name() { return "from extension"; }
            }
            let p = Person { name: "Alice" };
            let result = p.name;
            """);
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void ExtendStruct_CanMutateSelfFields()
    {
        var result = Run("""
            struct Counter { value }
            extend Counter {
                fn increment() {
                    self.value = self.value + 1;
                    return self.value;
                }
            }
            let c = Counter { value: 0 };
            c.increment();
            c.increment();
            let result = c.increment();
            """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void ExtendStruct_MultipleInstances_ExtensionOnAll()
    {
        var result = Run("""
            struct Point { x, y }
            extend Point {
                fn sum() { return self.x + self.y; }
            }
            let p1 = Point { x: 1, y: 2 };
            let p2 = Point { x: 10, y: 20 };
            let result = p1.sum() + p2.sum();
            """);
        Assert.Equal(33L, result);
    }

    [Fact]
    public void ExtendStruct_ExtensionAfterInstantiation_Works()
    {
        var result = Run("""
            struct Box { size }
            let b = Box { size: 10 };
            extend Box {
                fn doubled() { return self.size * 2; }
            }
            let result = b.doubled();
            """);
        Assert.Equal(20L, result);
    }

    // ── 7. Method Resolution Order ────────────────────────────────────────────

    [Fact]
    public void MRO_OriginalStructMethod_WinsOverExtension()
    {
        var result = Run("""
            struct MyType { value,
                fn describe() { return "original: " + self.value; }
            }
            extend MyType {
                fn describe() { return "extended: " + self.value; }
            }
            let m = MyType { value: 42 };
            let result = m.describe();
            """);
        Assert.Equal("original: 42", result);
    }

    [Fact]
    public void MRO_ExtensionBeforeUfcs_ExtensionWins()
    {
        var result = Run("""
            extend string {
                fn upper() { return "custom_upper"; }
            }
            let result = "hello".upper();
            """);
        Assert.Equal("custom_upper", result);
    }

    [Fact]
    public void MRO_LastExtendBlockWins_OnConflict()
    {
        var result = Run("""
            extend string {
                fn tag() { return "first"; }
            }
            extend string {
                fn tag() { return "second"; }
            }
            let result = "x".tag();
            """);
        Assert.Equal("second", result);
    }

    [Fact]
    public void MRO_StructFieldBeforeExtension_FieldWins()
    {
        var result = Run("""
            struct Config { host }
            extend Config {
                fn host() { return "ext_host"; }
            }
            let c = Config { host: "localhost" };
            let result = c.host;
            """);
        Assert.Equal("localhost", result);
    }

    [Fact]
    public void MRO_UfcsStillWorksAfterExtension_ForOtherMethods()
    {
        var result = Run("""
            extend string {
                fn shout() { return self + "!!!"; }
            }
            let result = "hello".upper();
            """);
        Assert.Equal("HELLO", result);
    }

    // ── 8. Scoping and Self ───────────────────────────────────────────────────

    [Fact]
    public void ExtendSelf_BoundCorrectly_ForBuiltInType()
    {
        var result = Run("""
            extend string {
                fn identity() { return self; }
            }
            let result = "test_value".identity();
            """);
        Assert.Equal("test_value", result);
    }

    [Fact]
    public void ExtendSelf_BoundCorrectly_ForStruct()
    {
        var result = Run("""
            struct Node { data }
            extend Node {
                fn getData() { return self.data; }
            }
            let n = Node { data: 42 };
            let result = n.getData();
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void ExtendSelf_ClosureCapture_AccessesOuterScope()
    {
        var result = Run("""
            let prefix = ">>>";
            extend string {
                fn prefixed() { return prefix + self; }
            }
            let result = "hello".prefixed();
            """);
        Assert.Equal(">>>hello", result);
    }

    [Fact]
    public void ExtendSelf_CanCallOtherExtensionMethods_OnSelf()
    {
        var result = Run("""
            extend string {
                fn shout() { return self.upper() + "!"; }
                fn doubleShout() { return self.shout() + self.shout(); }
            }
            let result = "hi".doubleShout();
            """);
        Assert.Equal("HI!HI!", result);
    }

    [Fact]
    public void ExtendSelf_CanUseUfcsInsideExtension()
    {
        var result = Run("""
            extend string {
                fn capitalizeAndTrim() { return self.trim().capitalize(); }
            }
            let result = "  hello world  ".capitalizeAndTrim();
            """);
        Assert.Equal("Hello world", result);
    }

    // ── 9. Error Cases ────────────────────────────────────────────────────────

    [Fact]
    public void ExtendError_UnknownType_ThrowsRuntimeError()
    {
        RunExpectingError("""
            extend UnknownType {
                fn foo() { return 1; }
            }
            """);
    }

    [Fact]
    public void ExtendError_ExtendBool_ThrowsRuntimeError()
    {
        RunExpectingError("""
            extend bool {
                fn flip() { return !self; }
            }
            """);
    }

    [Fact]
    public void ExtendError_NonFnInBody_ThrowsParseError()
    {
        RunExpectingParseError("""
            extend string { let x = 5; }
            """);
    }

    [Fact]
    public void ExtendError_FieldInBody_ThrowsParseError()
    {
        RunExpectingParseError("""
            extend string { name, }
            """);
    }

    [Fact]
    public void ExtendError_EmptyExtend_IsValid()
    {
        var result = Run("""
            extend string { }
            let result = "hello".upper();
            """);
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void ExtendError_ForwardReferencedStruct_ThrowsRuntimeError()
    {
        RunExpectingError("""
            extend Foo { fn bar() { return 1; } }
            struct Foo { x }
            """);
    }

    [Fact]
    public void ExtendError_ExtensionOnNull_ThrowsRuntimeError()
    {
        RunExpectingError("""
            extend string {
                fn greet() { return "hi"; }
            }
            let n = null;
            n.greet();
            """);
    }

    [Fact]
    public void ExtendError_InsideFunction_ThrowsRuntimeError()
    {
        RunExpectingError("""
            fn setup() {
                extend string {
                    fn foo() { return "foo"; }
                }
            }
            setup();
            """);
    }

    [Fact]
    public void ExtendError_InsideIfBlock_ThrowsRuntimeError()
    {
        RunExpectingError("""
            if (true) {
                extend string {
                    fn foo() { return "foo"; }
                }
            }
            """);
    }

    [Fact]
    public void ExtendError_MissingOpenBrace_ThrowsParseError()
    {
        RunExpectingParseError("""
            extend string fn foo() { }
            """);
    }

    // ── 10. Integration / Complex Scenarios ───────────────────────────────────

    [Fact]
    public void ExtendIntegration_StringPipeline_ChainsMultipleExtensions()
    {
        var result = Run("""
            extend string {
                fn exclaim() { return self + "!"; }
                fn wrap(tag) { return tag + self + tag; }
            }
            let result = "hello".upper().exclaim().wrap("**");
            """);
        Assert.Equal("**HELLO!**", result);
    }

    [Fact]
    public void ExtendIntegration_ArrayWithStructs_Works()
    {
        var result = Run("""
            struct Product { name, price }
            extend array {
                fn totalPrice() {
                    let total = 0.0;
                    for (let item in self) {
                        total = total + item.price;
                    }
                    return total;
                }
            }
            let products = [
                Product { name: "A", price: 10.0 },
                Product { name: "B", price: 20.0 },
                Product { name: "C", price: 5.0 }
            ];
            let result = products.totalPrice();
            """);
        Assert.Equal(35.0, result);
    }

    [Fact]
    public void ExtendIntegration_ExtensionUsesNamespaceFunction_Works()
    {
        // Extension method calls a stdlib namespace function (str.upper) with self as argument
        var result = Run("""
            extend string {
                fn shoutWithLength() {
                    return str.upper(self) + "@" + conv.toStr(len(self));
                }
            }
            let result = "hi".shoutWithLength();
            """);
        Assert.Equal("HI@2", result);
    }

    [Fact]
    public void ExtendIntegration_MultipleTypesExtended_AllWork()
    {
        var result = Run("""
            extend string { fn shout() { return self.upper() + "!"; } }
            extend array { fn total() { return arr.sum(self); } }
            extend int { fn doubled() { return self * 2; } }
            let n = [1, 2, 3].total();
            let x = 5.doubled();
            let result = n + x;
            """);
        Assert.Equal(16L, result);
    }

    [Fact]
    public void ExtendIntegration_ExtensionReturnsStruct_CanChainToStructMethod()
    {
        var result = Run("""
            struct Box { size }
            extend Box {
                fn doubled() { return Box { size: self.size * 2 }; }
                fn getSize() { return self.size; }
            }
            let b = Box { size: 7 };
            let result = b.doubled().getSize();
            """);
        Assert.Equal(14L, result);
    }
}
