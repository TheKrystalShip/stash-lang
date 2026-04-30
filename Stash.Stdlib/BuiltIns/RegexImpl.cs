namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Shared regex-result builder used by both <c>re.*</c> and the deprecated <c>str.*</c> capture shims.
/// </summary>
internal static class RegexImpl
{
    internal static StashInstance BuildRegexMatch(Match m)
    {
        var groups = new List<StashValue>(m.Groups.Count);
        var namedGroups = new StashDictionary();

        for (int i = 0; i < m.Groups.Count; i++)
        {
            Group g = m.Groups[i];
            string? groupName = null;

            string gName = m.Groups.Keys.ElementAtOrDefault(i) ?? i.ToString();
            if (gName != i.ToString())
                groupName = gName;

            var groupInstance = new StashInstance("RegexGroup", new Dictionary<string, StashValue>
            {
                ["value"]  = g.Success ? StashValue.FromObj(g.Value) : StashValue.Null,
                ["index"]  = g.Success ? StashValue.FromInt(g.Index) : StashValue.FromInt(-1L),
                ["length"] = g.Success ? StashValue.FromInt(g.Length) : StashValue.FromInt(0L),
                ["name"]   = groupName != null ? StashValue.FromObj(groupName) : StashValue.Null,
            });

            groups.Add(StashValue.FromObj(groupInstance));

            if (groupName != null && g.Success)
                namedGroups.Set(groupName, StashValue.FromObj(g.Value));
        }

        return new StashInstance("RegexMatch", new Dictionary<string, StashValue>
        {
            ["value"]       = StashValue.FromObj(m.Value),
            ["index"]       = StashValue.FromInt(m.Index),
            ["length"]      = StashValue.FromInt(m.Length),
            ["groups"]      = StashValue.FromObj(groups),
            ["namedGroups"] = StashValue.FromObj(namedGroups),
        });
    }
}
