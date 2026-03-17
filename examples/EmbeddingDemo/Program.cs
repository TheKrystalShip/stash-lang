using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using Stash.Interpreting;

// ─── Embedding Demo ───
// This shows how to embed the Stash scripting language in a C# application.
// Stash scripts can call C# functions, read host variables, and produce output
// that the host captures — similar to how Lua is embedded in games.

Console.WriteLine("=== Stash Embedding Demo ===\n");

// ── 1. Create a sandboxed engine ──
// StashCapabilities controls what scripts can access.
// Here we allow only safe operations — no filesystem, process, or network access.
var engine = new StashEngine(StashCapabilities.None);

// Redirect script output to a StringWriter so we can capture it
var output = new StringWriter();
engine.Output = output;

// ── 2. Inject host variables ──
// Scripts can read these as regular variables
engine.SetGlobal("playerName", "Alice");
engine.SetGlobal("playerLevel", 42L);   // Stash integers are C# longs
engine.SetGlobal("difficulty", "hard");

// ── 3. Inject C# functions ──
// Scripts can call these like any Stash function
engine.SetGlobal("getPlayerStats", engine.CreateFunction("getPlayerStats", 0, (args) =>
{
    // Return data as a Stash array (List<object?>)
    return new List<object?> { "hp", 100L, "mp", 50L, "xp", 2340L };
}));

engine.SetGlobal("log", engine.CreateFunction("log", 1, (args) =>
{
    Console.WriteLine($"  [Host Log] {args[0]}");
    return null;
}));

// ── 4. Run Stash code ──
// Note: use conv.toStr() to convert numbers to strings; Stash strings use double quotes
var result = engine.Run("""
    let greeting = "Welcome, " + playerName + "! (Level " + conv.toStr(playerLevel) + ")";
    io.println(greeting);
    io.println("Difficulty: " + difficulty);

    // Call the host function
    log("Player entered the dungeon");

    // Define a function in Stash that the host can call later
    fn formatScore(name, score) {
        return name + ": " + conv.toStr(score) + " points";
    }
    """);

if (result.Success)
{
    Console.WriteLine("Script output:");
    Console.WriteLine($"  {output.ToString().TrimEnd().Replace("\n", "\n  ")}");
}
else
{
    Console.WriteLine($"Script errors: {string.Join(", ", result.Errors)}");
}

// ── 5. Evaluate expressions ──
Console.WriteLine("\nExpression evaluation:");

var evalResult = engine.Evaluate("playerLevel * 100");
Console.WriteLine($"  playerLevel * 100 = {evalResult.Value}");

evalResult = engine.Evaluate("\"Hello, \" + playerName");
Console.WriteLine($"  \"Hello, \" + playerName = {evalResult.Value}");

// ── 6. Read back script-defined values ──
Console.WriteLine("\nReading script state:");

var greeting = engine.GetGlobal("greeting");
Console.WriteLine($"  greeting = {greeting}");

// ── 7. Error handling ──
Console.WriteLine("\nError handling:");

var badResult = engine.Run("let x = 1 / 0;");
Console.WriteLine($"  Division by zero: Success={badResult.Success}, Error={badResult.Errors[0]}");

var parseErr = engine.Run("let = ;");
Console.WriteLine($"  Parse error: Success={parseErr.Success}, Error={parseErr.Errors[0]}");

// ── 8. Step limits ──
Console.WriteLine("\nStep limits:");

var limitedEngine = new StashEngine(StashCapabilities.None);
limitedEngine.StepLimit = 100;
limitedEngine.Output = new StringWriter(); // suppress output

var limitResult = limitedEngine.Run("let x = 0; while (true) { x = x + 1; }");
Console.WriteLine($"  Infinite loop with StepLimit=100: Success={limitResult.Success}");
Console.WriteLine($"  Error: {limitResult.Errors[0]}");
Console.WriteLine($"  Steps executed: {limitedEngine.StepCount}");

// ── 9. Cancellation token ──
Console.WriteLine("\nCancellation token:");

var cts = new CancellationTokenSource();
var cancelEngine = new StashEngine(StashCapabilities.None);
cancelEngine.Output = new StringWriter();
cancelEngine.CancellationToken = cts.Token;

// Cancel immediately to demonstrate
cts.Cancel();
var cancelResult = cancelEngine.Run("let x = 0; while (true) { x = x + 1; }");
Console.WriteLine($"  Pre-cancelled script: Success={cancelResult.Success}");
Console.WriteLine($"  Error: {cancelResult.Errors[0]}");

// ── 10. Script pre-compilation ──
Console.WriteLine("\nScript pre-compilation:");

var compileEngine = new StashEngine(StashCapabilities.None);
var compileOutput = new StringWriter();
compileEngine.Output = compileOutput;

var script = compileEngine.Compile("io.println(\"Compiled script executed!\");");
if (script != null)
{
    compileEngine.Run(script);
    compileEngine.Run(script); // re-execute without re-parsing
    Console.WriteLine($"  Ran compiled script twice: {compileOutput.ToString().TrimEnd().Replace("\n", ", ")}");
}

// ── 11. Type marshalling ──
Console.WriteLine("\nType marshalling:");

var marshalEngine = new StashEngine(StashCapabilities.None);
marshalEngine.Output = new StringWriter();

marshalEngine.SetGlobal("config", marshalEngine.CreateDictionary(new Dictionary<string, object?>
{
    ["host"] = "localhost",
    ["port"] = 8080L,
    ["debug"] = true,
}));

marshalEngine.Run("let port = dict.get(config, \"port\");");
var portValue = marshalEngine.GetGlobal("port");
Console.WriteLine($"  Injected dict, read back port: {portValue}");

marshalEngine.Run(@"
    let serverInfo = dict.new();
    dict.set(serverInfo, ""name"", ""GameServer-1"");
    dict.set(serverInfo, ""players"", 12);
");

var serverDict = marshalEngine.ToDictionary(marshalEngine.GetGlobal("serverInfo"));
Console.WriteLine($"  Script dict \u2192 C#: name={serverDict["name"]}, players={serverDict["players"]}");

Console.WriteLine("\n=== Demo Complete ===");
