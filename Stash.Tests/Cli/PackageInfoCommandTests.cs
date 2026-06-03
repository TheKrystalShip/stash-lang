using System;
using System.IO;
using Stash.Cli.PackageManager.Commands;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Tests for <see cref="InfoCommand.Render"/> output shape after
/// the owners field was removed from the package-detail response (P1).
/// </summary>
[Collection("CliTests")]
public sealed class PackageInfoCommandTests
{
    // ── Console capture helper ────────────────────────────────────────────────

    private static string CaptureRender(string json)
    {
        var sw = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(sw);
            InfoCommand.Render(json);
        }
        finally
        {
            Console.SetOut(original);
        }
        return sw.ToString();
    }

    // ── Baseline JSON with owners array present ───────────────────────────────

    private const string JsonWithOwners = """
        {
            "name": "@org/my-lib",
            "latest": "1.0.0",
            "description": "A library",
            "license": "MIT",
            "owners": ["alice", "bob"],
            "keywords": [],
            "versions": {
                "1.0.0": {
                    "version": "1.0.0",
                    "dependencies": {},
                    "integrity": "sha256-abc",
                    "publishedAt": "2024-01-01T00:00:00Z",
                    "publishedBy": "alice"
                }
            },
            "createdAt": "2024-01-01T00:00:00Z",
            "updatedAt": "2024-01-02T00:00:00Z"
        }
        """;

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_JsonWithOwnersArray_DoesNotPrintOwnersSection()
    {
        // Feed Render a response that still carries an "owners" key (e.g. a cached
        // response from an older server).  The render block was deleted, so it must
        // not appear in the output.
        string output = CaptureRender(JsonWithOwners);

        Assert.DoesNotContain("Owners:", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", output);
        Assert.DoesNotContain("bob", output);
    }

    [Fact]
    public void Render_JsonWithOwnersArray_StillPrintsOtherFields()
    {
        // Confirm the removal didn't accidentally break the rest of the render.
        string output = CaptureRender(JsonWithOwners);

        Assert.Contains("@org/my-lib", output);
        Assert.Contains("Latest: 1.0.0", output);
        Assert.Contains("Description: A library", output);
        Assert.Contains("License: MIT", output);
        Assert.Contains("Versions:", output);
        Assert.Contains("1.0.0", output);
    }

    [Fact]
    public void Render_JsonWithoutOwnersKey_ProducesNoOwnersSection()
    {
        // Modern server response — no owners key at all.
        const string json = """
            {
                "name": "@org/pkg",
                "latest": "2.0.0",
                "keywords": [],
                "versions": {},
                "createdAt": "2024-01-01T00:00:00Z",
                "updatedAt": "2024-01-02T00:00:00Z"
            }
            """;

        string output = CaptureRender(json);

        Assert.DoesNotContain("Owners:", output, StringComparison.OrdinalIgnoreCase);
    }
}
