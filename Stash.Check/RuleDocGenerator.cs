namespace Stash.Check;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Stash.Analysis;

/// <summary>
/// Generates one markdown documentation page per diagnostic code into an output directory,
/// plus an <c>index.md</c> listing all rules grouped by category.
/// </summary>
internal static class RuleDocGenerator
{
    public static void GenerateDocs(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var descriptors = DiagnosticDescriptors.AllByCode.Values
            .OrderBy(d => d.Code, StringComparer.Ordinal)
            .ToList();

        foreach (var descriptor in descriptors)
        {
            string path = Path.Combine(outputDir, $"{descriptor.Code}.md");
            File.WriteAllText(path, BuildRulePage(descriptor), Encoding.UTF8);
        }

        string indexPath = Path.Combine(outputDir, "index.md");
        File.WriteAllText(indexPath, BuildIndexPage(descriptors), Encoding.UTF8);

        Console.WriteLine($"Generated {descriptors.Count} rule pages and index.md in '{outputDir}'.");
    }

    private static string BuildRulePage(DiagnosticDescriptor d)
    {
        string fixable = d.DefaultFixApplicability.HasValue
            ? $"Yes ({d.DefaultFixApplicability.Value})"
            : "No";

        var sb = new StringBuilder();
        sb.AppendLine($"# {d.Code}: {d.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Severity:** {d.DefaultLevel} | **Category:** {d.Category} | **Fixable:** {fixable}");
        sb.AppendLine();
        sb.AppendLine("## Description");
        sb.AppendLine();
        sb.AppendLine(d.MessageFormat);
        sb.AppendLine();
        sb.AppendLine("## Examples");
        sb.AppendLine();
        sb.AppendLine("### Incorrect");
        sb.AppendLine();
        sb.AppendLine("```stash");
        sb.AppendLine("// Code that triggers this diagnostic");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### Correct");
        sb.AppendLine();
        sb.AppendLine("```stash");
        sb.AppendLine("// Code that doesn't trigger this diagnostic");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Configuration");
        sb.AppendLine();
        sb.AppendLine("This rule can be configured in `.stashcheck`:");
        sb.AppendLine();
        sb.AppendLine("```ini");
        sb.AppendLine($"severity.{d.Code} = off    # Disable this rule");
        sb.AppendLine($"severity.{d.Code} = warning  # Change severity");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Suppression");
        sb.AppendLine();
        sb.AppendLine("```stash");
        sb.AppendLine($"// stash-disable-next-line {d.Code}");
        sb.AppendLine("problematic_code()");
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static string BuildIndexPage(IReadOnlyList<DiagnosticDescriptor> descriptors)
    {
        var byCategory = descriptors
            .GroupBy(d => d.Category)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("# Stash Diagnostic Rules");
        sb.AppendLine();
        sb.AppendLine($"There are {descriptors.Count} rules across {descriptors.Select(d => d.Category).Distinct().Count()} categories.");
        sb.AppendLine();

        foreach (var group in byCategory)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Code | Title | Severity | Fixable |");
            sb.AppendLine("|------|-------|----------|---------|");

            foreach (var d in group.OrderBy(x => x.Code, StringComparer.Ordinal))
            {
                string fixable = d.DefaultFixApplicability.HasValue
                    ? $"Yes ({d.DefaultFixApplicability.Value})"
                    : "No";
                sb.AppendLine($"| [{d.Code}]({d.Code}.md) | {d.Title} | {d.DefaultLevel} | {fixable} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
