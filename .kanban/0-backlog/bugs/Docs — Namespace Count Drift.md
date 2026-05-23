# Docs — Namespace Count Drift

**Status:** Backlog — Bug
**Created:** 2026-04-06
**Discovery context:** Found during review of "Stdlib Extraction — Move log and store to Packages"

---

## Bug Description

Multiple documentation files reference a hardcoded namespace count that has drifted out of sync with the actual number of registered namespaces in `StdlibDefinitions.BuildNamespaces()`.

### Actual Count

`StdlibDefinitions.BuildNamespaces()` registers **30 namespace definitions**:

io, conv, env, process, fs, path, arr, dict, str, assert, test, math, time, json, http, ini, yaml, toml, config, tpl, args, crypto, encoding, term, sys, pkg, task, ssh, sftp, net

### Current Documentation Says

All these files say **"24 namespaces"** (after the log/store removal updated 26 → 24):

- `.github/copilot-instructions.md` — 3 occurrences (line 3, line 10, line 59)
- `.github/instructions/stdlib.instructions.md` — heading says "All 24 Namespaces", table lists 23 entries (missing pkg, yaml, toml, task, net, ssh, sftp)
- `docs/Playground — Browser Playground.md` — "All 24 built-in namespaces"

### Root Cause

The namespace count was originally set to some value and has not been kept in sync as new namespaces were added (yaml, toml, pkg, task, net, ssh, sftp). The log/store removal correctly subtracted 2 from the previous count but the base count was already stale.

### Fix

1. Update all three files to reflect the actual count (30, or whatever it is at fix time)
2. Update the namespace table in `stdlib.instructions.md` to include all missing namespaces
3. Update the capability gate table to include any missing entries
4. Consider whether the Playground tokenizer (`stash-language.js`) also needs the new namespaces added — currently it lists 22 entries, missing yaml, toml, pkg, task, net, ssh, sftp

### Affected Files

- `.github/copilot-instructions.md`
- `.github/instructions/stdlib.instructions.md`
- `docs/Playground — Browser Playground.md`
- `Stash.Playground/wwwroot/js/stash-language.js` (tokenizer namespace list)
