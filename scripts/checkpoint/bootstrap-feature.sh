#!/usr/bin/env bash
# bootstrap-feature.sh <slug> [title]
#
# Creates .kanban/2-in-progress/<slug>/ from templates with the slug substituted.
# Used by the architect after the spec is approved.
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "usage: $0 <slug> [title]" >&2
  exit 2
fi

slug="$1"
title="${2:-${slug//-/ }}"

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
templates="$repo_root/.kanban/_templates"
dest="$repo_root/.kanban/2-in-progress/$slug"

if [ -e "$dest" ]; then
  echo "error: $dest already exists" >&2
  exit 1
fi

mkdir -p "$dest"

# Copy templates and substitute placeholders.
sed -e "s/{{Feature Name}}/$title/g" \
    -e "s/{{feature-slug}}/$slug/g" \
    -e "s/{{user}}/$(git config user.name 2>/dev/null || echo unknown)/g" \
    -e "s/{{YYYY-MM-DD}}/$(date -u +%Y-%m-%d)/g" \
    "$templates/brief-template.md" > "$dest/brief.md"

# plan.yaml needs the slug in the `feature:` field.
sed -e "s/^feature: feature-slug$/feature: $slug/" \
    -e "s/^title: Feature Title$/title: $title/" \
    -e "s/^created: 2026-05-16$/created: $(date -u +%Y-%m-%d)/" \
    "$templates/plan-template.yaml" > "$dest/plan.yaml"

sed -e "s/^feature: feature-slug$/feature: $slug/" \
    -e "s/^updated: 2026-05-16T00:00:00Z$/updated: $(date -u +%Y-%m-%dT00:00:00Z)/" \
    "$templates/checkpoint-template.yaml" > "$dest/checkpoint.yaml"

echo "created: $dest"
echo "next: edit brief.md and plan.yaml, then run python3 scripts/checkpoint/validate-spec.py $slug"
