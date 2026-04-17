using System;
using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Runtime;
using Stash.Runtime.Protocols;
using Stash.Runtime.Types;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Integration tests for the external type registration system (IVMTypeRegistrar).
/// Uses a synthetic "Vector2D" type to verify typeof and is operator support.
/// </summary>
public class TypeRegistrationTests
{
    /// <summary>
    /// A synthetic external type for testing type registration.
    /// Does NOT implement any IVM* protocols — it's a plain CLR class.
    /// </summary>
    private sealed class Vector2D
    {
        public double X { get; }
        public double Y { get; }

        public Vector2D(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    private StashEngine CreateEngineWithVector()
    {
        var engine = new StashEngine();
        engine.RegisterType<Vector2D>("vector2d");
        return engine;
    }

    [Fact]
    public void TypeOf_RegisteredType_ReturnsRegisteredName()
    {
        var engine = CreateEngineWithVector();
        engine.SetGlobal("v", new Vector2D(1.0, 2.0));
        object? result = engine.Evaluate("typeof(v);").Value;
        Assert.Equal("vector2d", result);
    }

    [Fact]
    public void Is_RegisteredType_ReturnsTrue()
    {
        var engine = CreateEngineWithVector();
        engine.SetGlobal("v", new Vector2D(1.0, 2.0));
        object? result = engine.Evaluate("v is \"vector2d\";").Value;
        Assert.Equal(true, result);
    }

    [Fact]
    public void Is_RegisteredType_WrongValue_ReturnsFalse()
    {
        var engine = CreateEngineWithVector();
        engine.SetGlobal("v", "not a vector");
        object? result = engine.Evaluate("v is \"vector2d\";").Value;
        Assert.Equal(false, result);
    }

    [Fact]
    public void TypeOf_UnregisteredType_ReturnsUnknown()
    {
        var engine = new StashEngine();
        engine.SetGlobal("v", new Vector2D(1.0, 2.0));
        object? result = engine.Evaluate("typeof(v);").Value;
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void RegisterType_WithCustomPredicate_Works()
    {
        var engine = new StashEngine();
        engine.RegisterType<Vector2D>("unit_vector", obj => obj is Vector2D vec && Math.Abs(vec.X * vec.X + vec.Y * vec.Y - 1.0) < 0.0001);
        engine.SetGlobal("v1", new Vector2D(1.0, 0.0));
        engine.SetGlobal("v2", new Vector2D(3.0, 4.0));
        Assert.Equal(true, engine.Evaluate("v1 is \"unit_vector\";").Value);
        Assert.Equal(false, engine.Evaluate("v2 is \"unit_vector\";").Value);
    }

    [Fact]
    public void RegisterType_AfterExecution_Throws()
    {
        var engine = new StashEngine();
        engine.Run("let x = 1;");
        Assert.Throws<InvalidOperationException>(() => engine.RegisterType<Vector2D>("vector2d"));
    }
}
