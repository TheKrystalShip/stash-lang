#!/usr/bin/env bash
# gen-bash-parity-fixture.sh
#
# Generates (or drift-checks) the bash-decision parity fixture:
#   Stash.Tests/Stdlib/Fixtures/path-match-bash-parity.tsv
#
# Each row:  pattern<TAB>path<TAB>expected_bool
#
# The "expected_bool" comes exclusively from bash [[ "$path" == $pattern ]]
# under shopt -s globstar nullglob extglob -- never hand-authored, never from
# filesystem expansion.  This file is the oracle that PathMatchBashParityTests
# validates path.match against.
#
# Usage:
#   ./gen-bash-parity-fixture.sh          # write fixture in place
#   ./gen-bash-parity-fixture.sh --check  # diff against committed; exit 1 if different
#
# Requirements: bash >= 4, python3 (with PyYAML), sort.

set -euo pipefail
shopt -s globstar nullglob extglob

# ---------------------------------------------------------------------------
# Resolve repo root relative to this script (works from any cwd).
# ---------------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
FIXTURE_PATH="$REPO_ROOT/Stash.Tests/Stdlib/Fixtures/path-match-bash-parity.tsv"

CHECK_MODE=0
if [[ "${1:-}" == "--check" ]]; then
    CHECK_MODE=1
fi

# ---------------------------------------------------------------------------
# Step 1: Harvest patterns from plan.yaml files via python3 + PyYAML.
# ---------------------------------------------------------------------------
PATTERNS_FILE="$(mktemp)"
trap 'rm -f "$PATTERNS_FILE"' EXIT

python3 - <<'PYEOF' "$REPO_ROOT" "$PATTERNS_FILE"
import sys, glob, os, yaml

repo_root = sys.argv[1]
out_path  = sys.argv[2]

patterns = set()

plan_files = (
    glob.glob(os.path.join(repo_root, '.kanban/4-done/*/plan.yaml')) +
    glob.glob(os.path.join(repo_root, '.kanban/2-in-progress/*/plan.yaml'))
)

for pf in sorted(plan_files):
    try:
        with open(pf) as f:
            doc = yaml.safe_load(f)
    except Exception as e:
        print(f"[warn] could not parse {pf}: {e}", file=sys.stderr)
        continue
    if not doc or not isinstance(doc, dict):
        continue

    # Top-level scope:
    for p in (doc.get('scope') or []):
        if isinstance(p, str):
            p = p.strip()
            if p:
                patterns.add(p)

    # Per-phase files: and scope:
    for phase in (doc.get('phases') or []):
        if not isinstance(phase, dict):
            continue
        for key in ('files', 'scope'):
            for p in (phase.get(key) or []):
                if isinstance(p, str):
                    p = p.strip()
                    if p:
                        patterns.add(p)

with open(out_path, 'w') as f:
    for p in sorted(patterns):
        f.write(p + '\n')
PYEOF

# ---------------------------------------------------------------------------
# Step 2: Build (pattern, path) candidate pairs.
#
# For each harvested pattern we generate representative candidate paths that
# should match or not match.  The bash oracle fills the verdict column -- we
# never hand-author it.
#
# Candidates are produced by two complementary strategies:
#   A) Mechanical transformations of the pattern itself.
#   B) A fixed set of synthesized rows covering each required construct.
# ---------------------------------------------------------------------------

# Collect rows in an associative array keyed by "pattern TAB path" to dedup.
declare -A VERDICT_MAP

record_pair() {
    local pat="$1" path="$2"
    local key="$pat	$path"
    # Skip if already recorded.
    if [[ -n "${VERDICT_MAP[$key]+x}" ]]; then
        return
    fi
    local verdict
    if [[ "$path" == $pat ]]; then
        verdict=true
    else
        verdict=false
    fi
    VERDICT_MAP["$key"]="$verdict"
}

# ---------- Strategy A: transform harvested patterns ----------------------
while IFS= read -r pat || [[ -n "$pat" ]]; do
    [[ -z "$pat" ]] && continue

    # Skip patterns that look like prose (contain spaces) -- these are
    # done_when / notes strings that leaked, not file glob patterns.
    if [[ "$pat" == *' '* ]]; then
        continue
    fi

    # Skip extglob patterns (path.match rejects them; they cannot produce a
    # bool from the pure matcher, so they must not appear in the fixture).
    if [[ "$pat" =~ ^[!@+?*]\( ]]; then
        continue
    fi
    # Also catch interior extglob triggers.
    if [[ "$pat" =~ [!@+?*]\( ]]; then
        continue
    fi

    # Derive candidate paths from the pattern:

    # 1. The literal pattern itself (may or may not match depending on metas).
    record_pair "$pat" "$pat"

    # 2. Strip trailing /** and use as a path (should NOT match pat if pat ends /**).
    base="${pat%%/**}"
    if [[ "$base" != "$pat" ]]; then
        # e.g. pat=Stash.Stdlib/** => base=Stash.Stdlib  => should be FALSE
        record_pair "$pat" "$base"
        # A file one level deep => should be TRUE
        record_pair "$pat" "${base}/File.cs"
        # A file two levels deep => should be TRUE
        record_pair "$pat" "${base}/Sub/File.cs"
        # Something entirely different => should be FALSE
        record_pair "$pat" "Other/File.cs"
    fi

    # 3. If pattern ends /*.ext, pair with path at that level and deeper.
    if [[ "$pat" =~ ^([^*?[]+)/\*\.([^*?[/]+)$ ]]; then
        dir="${BASH_REMATCH[1]}"
        ext="${BASH_REMATCH[2]}"
        record_pair "$pat" "${dir}/file.${ext}"
        record_pair "$pat" "${dir}/sub/file.${ext}"
        record_pair "$pat" "${dir}/file.other"
        record_pair "$pat" "Other/file.${ext}"
    fi

    # 4. If pattern is a literal file path (no metacharacters), try a
    #    neighbour path to get a FALSE row.
    if [[ "$pat" != *'*'* && "$pat" != *'?'* && "$pat" != *'['* ]]; then
        record_pair "$pat" "$pat"  # true (literal match)
        record_pair "$pat" "${pat}.extra"  # false
        dir="$(dirname "$pat")"
        if [[ "$dir" != "." && "$dir" != "$pat" ]]; then
            record_pair "$pat" "${dir}/other.cs"
        fi
    fi

    # 5. Glob with wildcard dir (e.g. Stash.Tests/**/*.cs).
    if [[ "$pat" =~ ^([^*?[]+)/\*\*/([^*?[/]+)$ ]]; then
        root="${BASH_REMATCH[1]}"
        tail="${BASH_REMATCH[2]}"
        record_pair "$pat" "${root}/direct/${tail}"
        record_pair "$pat" "${root}/a/b/${tail}"
        record_pair "$pat" "Other/a/${tail}"
    fi

done < "$PATTERNS_FILE"

# ---------- Strategy B: synthesized edge-case rows (construct coverage) ----
#
# These rows ensure the shape-coverage [Fact] can always find at least one
# row per required construct regardless of what the harvested corpus contains.
#
# B1: ** construct (cross-segment match and non-match)
record_pair "Stash.Stdlib/**"              "Stash.Stdlib/Foo.cs"
record_pair "Stash.Stdlib/**"              "Stash.Stdlib/Sub/Bar.cs"
record_pair "Stash.Stdlib/**"              "Stash.Stdlib/Sub/Deep/Baz.cs"
record_pair "Stash.Stdlib/**"             "Other/Baz.cs"
record_pair "Stash.Stdlib/**"             "Stash.Stdlib"
record_pair "a/**"                        "a/b.cs"
record_pair "a/**"                        "a/b/c.cs"
record_pair "a/**"                        "a"
record_pair "a/**"                        "b/c.cs"

# B2: * crossing / (lone * in pattern, path has extra slash -- expect true)
record_pair "a/*.cs"                      "a/file.cs"
record_pair "a/*.cs"                      "a/b/file.cs"
record_pair "a/*.cs"                      "a/file.txt"
record_pair "Stash.Analysis/Visitors/*.cs" "Stash.Analysis/Visitors/Sub/Foo.cs"
record_pair "Stash.Analysis/Visitors/*.cs" "Stash.Analysis/Visitors/Foo.cs"
record_pair "Stash.Analysis/Visitors/*.cs" "Other/Foo.cs"

# B3: ? construct (matches any single character including /)
record_pair "a/?.cs"                      "a/b.cs"
record_pair "a/?.cs"                      "a/bc.cs"
record_pair "a/?.cs"                      "a/.cs"
record_pair "Stash.?ore/**"              "Stash.Core/Foo.cs"
record_pair "Stash.?ore/**"              "Stash.Bore/Foo.cs"
record_pair "Stash.?ore/**"              "Stash.Score/Foo.cs"
record_pair "Stash.?ore/**"              "Stash.ore/Foo.cs"

# B4: character class [abc]
record_pair "[Ss]tash.Core/**"            "Stash.Core/Foo.cs"
record_pair "[Ss]tash.Core/**"            "stash.Core/Foo.cs"
record_pair "[Ss]tash.Core/**"            "Xtash.Core/Foo.cs"
record_pair "Stash.Core/[A-Z]*.cs"        "Stash.Core/Foo.cs"
record_pair "Stash.Core/[A-Z]*.cs"        "Stash.Core/foo.cs"
record_pair "Stash.Core/[A-Z]*.cs"        "Other/Foo.cs"

# B4b: negated character class [!x] and [^x]
record_pair "Stash.[!T]ore/**"            "Stash.Core/Foo.cs"
record_pair "Stash.[!T]ore/**"            "Stash.Tore/Foo.cs"
record_pair "Stash.[^T]ore/**"            "Stash.Core/Foo.cs"
record_pair "Stash.[^T]ore/**"            "Stash.Tore/Foo.cs"

# B5: literal-only patterns (no metacharacters)
record_pair "CHANGELOG.md"               "CHANGELOG.md"
record_pair "CHANGELOG.md"               "CHANGELOG.txt"
record_pair "CHANGELOG.md"               "other/CHANGELOG.md"
record_pair ".claude/repo.md"            ".claude/repo.md"
record_pair ".claude/repo.md"            ".claude/repo.yaml"
record_pair "Stash.Registry/Startup.cs"  "Stash.Registry/Startup.cs"
record_pair "Stash.Registry/Startup.cs"  "Stash.Registry/Startup.cs.bak"

# ---------------------------------------------------------------------------
# Step 3: Emit sorted TSV rows.
# ---------------------------------------------------------------------------

emit_rows() {
    for key in "${!VERDICT_MAP[@]}"; do
        local verdict="${VERDICT_MAP[$key]}"
        # key is "pattern\tpath"
        printf '%s\t%s\n' "$key" "$verdict"
    done
}

GENERATED="$(emit_rows | LC_ALL=C sort)"

# ---------------------------------------------------------------------------
# Step 4: Write or diff.
# ---------------------------------------------------------------------------

if [[ "$CHECK_MODE" -eq 1 ]]; then
    TMPFILE="$(mktemp)"
    trap 'rm -f "$TMPFILE" "$PATTERNS_FILE"' EXIT
    printf '%s\n' "$GENERATED" > "$TMPFILE"
    if diff -u "$FIXTURE_PATH" "$TMPFILE"; then
        echo "path-match-bash-parity.tsv is up to date."
        exit 0
    else
        echo ""
        echo "DRIFT DETECTED: committed fixture differs from regenerated output."
        echo "Regenerate with: bash scripts/path-match/gen-bash-parity-fixture.sh"
        exit 1
    fi
else
    mkdir -p "$(dirname "$FIXTURE_PATH")"
    printf '%s\n' "$GENERATED" > "$FIXTURE_PATH"
    COUNT="$(wc -l < "$FIXTURE_PATH")"
    echo "Wrote $COUNT rows to $FIXTURE_PATH"
fi
