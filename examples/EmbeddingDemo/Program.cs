using System;
using System.IO;
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

Console.WriteLine("\n=== Demo Complete ===");
