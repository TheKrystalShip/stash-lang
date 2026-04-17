namespace Stash.Runtime.Stdlib;

using System.Collections.Generic;
using Stash.Runtime.Types;

/// <summary>
/// Composes multiple IStdlibProviders into a single globals dictionary
/// suitable for VirtualMachine construction.
/// </summary>
public sealed class StdlibComposer
{
    private readonly List<IStdlibProvider> _providers = [];
    private readonly Dictionary<string, StashValue> _extraGlobals = [];
    private readonly HashSet<string> _excludedNames = [];
    private StashCapabilities _capabilities = StashCapabilities.All;

    /// <summary>Add a provider. Providers are evaluated in registration order.</summary>
    public StdlibComposer Add(IStdlibProvider provider)
    {
        _providers.Add(provider);
        return this;
    }

    /// <summary>Add a single global function or value directly.</summary>
    public StdlibComposer AddGlobal(string name, StashValue value)
    {
        _extraGlobals[name] = value;
        return this;
    }

    /// <summary>Add a single global built-in function with the zero-alloc handler.</summary>
    public StdlibComposer AddGlobal(string name, int arity, BuiltInFunction.DirectHandler handler)
    {
        var fn = new BuiltInFunction(name, arity, handler);
        _extraGlobals[name] = StashValue.FromObj(fn);
        return this;
    }

    /// <summary>Exclude specific namespace/global names from all providers.</summary>
    public StdlibComposer Exclude(params string[] names)
    {
        foreach (string name in names)
            _excludedNames.Add(name);
        return this;
    }

    /// <summary>
    /// Set the capabilities mask. Namespaces whose RequiredCapability
    /// is not satisfied are silently omitted.
    /// </summary>
    public StdlibComposer WithCapabilities(StashCapabilities capabilities)
    {
        _capabilities = capabilities;
        return this;
    }

    /// <summary>
    /// Build the final globals dictionary.
    /// Name collisions from later providers override earlier ones.
    /// Excluded names are removed after all providers contribute.
    /// </summary>
    public Dictionary<string, StashValue> Build()
    {
        var globals = new Dictionary<string, StashValue>();

        foreach (IStdlibProvider provider in _providers)
        {
            // Add namespaces
            foreach (StdlibNamespaceEntry entry in provider.GetNamespaces(_capabilities))
            {
                if (entry.RequiredCapability != StashCapabilities.None &&
                    !_capabilities.HasFlag(entry.RequiredCapability))
                    continue;

                if (!entry.Namespace.IsFrozen)
                    entry.Namespace.Freeze();

                globals[entry.Name] = StashValue.FromObj(entry.Namespace);
            }

            // Add standalone globals
            foreach (StdlibGlobalEntry entry in provider.GetGlobals(_capabilities))
            {
                if (entry.RequiredCapability != StashCapabilities.None &&
                    !_capabilities.HasFlag(entry.RequiredCapability))
                    continue;

                globals[entry.Name] = entry.Value;
            }
        }

        // Add extra globals (override providers)
        foreach (var (name, value) in _extraGlobals)
        {
            globals[name] = value;
        }

        // Remove excluded names
        foreach (string name in _excludedNames)
        {
            globals.Remove(name);
        }

        return globals;
    }

    /// <summary>
    /// Build and return both the globals dictionary and a StdlibManifest
    /// listing all contributed names (for embedding in .stashc).
    /// </summary>
    public (Dictionary<string, StashValue> Globals, StdlibManifest Manifest) BuildWithManifest()
    {
        Dictionary<string, StashValue> globals = Build();

        var namespaceNames = new List<string>();
        var globalNames = new List<string>();
        StashCapabilities minCaps = StashCapabilities.None;

        foreach (var (name, value) in globals)
        {
            if (value.Tag == StashValueTag.Obj && value.AsObj is StashNamespace)
            {
                namespaceNames.Add(name);
            }
            else
            {
                globalNames.Add(name);
            }
        }

        // Compute minimum capabilities from what was contributed
        foreach (IStdlibProvider provider in _providers)
        {
            foreach (StdlibNamespaceEntry entry in provider.GetNamespaces(_capabilities))
            {
                if (globals.ContainsKey(entry.Name))
                    minCaps |= entry.RequiredCapability;
            }

            foreach (StdlibGlobalEntry entry in provider.GetGlobals(_capabilities))
            {
                if (globals.ContainsKey(entry.Name))
                    minCaps |= entry.RequiredCapability;
            }
        }

        var manifest = new StdlibManifest(namespaceNames, globalNames, minCaps);
        return (globals, manifest);
    }
}
