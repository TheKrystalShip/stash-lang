namespace Stash.Stdlib;

using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
using Stash.Runtime.Stdlib;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;

/// <summary>
/// IStdlibProvider that wraps Stash's built-in standard library.
/// This is the bridge between the existing StdlibDefinitions registry
/// and the new provider protocol.
/// </summary>
public sealed class StashStdlibProvider : IStdlibProvider
{
    public IReadOnlyList<StdlibNamespaceEntry> GetNamespaces(StashCapabilities capabilities)
    {
        var definitions = StdlibDefinitions.GetNamespaces(capabilities);
        var entries = new List<StdlibNamespaceEntry>(definitions.Count);

        foreach (NamespaceDefinition def in definitions)
        {
            var functions = def.Functions.Count > 0
                ? def.Functions.Select(f => new StdlibFunctionMeta(
                    f.Name,
                    f.Parameters.Select(p => new StdlibParamMeta(p.Name, p.Type)).ToArray(),
                    f.ReturnType,
                    f.IsVariadic,
                    f.Documentation)).ToList()
                : null;

            var constants = def.Constants.Count > 0
                ? def.Constants.Select(c => new StdlibConstantMeta(
                    c.Name, c.Type, c.Value, c.Documentation)).ToList()
                : null;

            entries.Add(new StdlibNamespaceEntry(
                def.Name,
                def.Namespace,
                def.RequiredCapability,
                functions,
                constants));
        }

        return entries;
    }

    public IReadOnlyList<StdlibGlobalEntry> GetGlobals(StashCapabilities capabilities)
    {
        NamespaceDefinition globalDef = StdlibDefinitions.GetGlobalNamespace(capabilities);
        var entries = new List<StdlibGlobalEntry>();

        // Get all member values from the global namespace
        foreach (var (name, value) in globalDef.Namespace.GetAllMemberValues())
        {
            // Find matching function metadata if available
            StdlibFunctionMeta? meta = null;
            NamespaceFunction? funcDef = globalDef.Functions.FirstOrDefault(f => f.Name == name);
            if (funcDef is not null)
            {
                meta = new StdlibFunctionMeta(
                    funcDef.Name,
                    funcDef.Parameters.Select(p => new StdlibParamMeta(p.Name, p.Type)).ToArray(),
                    funcDef.ReturnType,
                    funcDef.IsVariadic,
                    funcDef.Documentation);
            }

            entries.Add(new StdlibGlobalEntry(name, value, StashCapabilities.None, meta));
        }

        return entries;
    }
}
