#!/usr/bin/env bash
# aetheric-forge/primitives is checked out twice in this tree: once directly (primitives/) and
# once transitively through contracts/primitives/ (aetheric-contracts has its own primitives
# submodule for the same reason). Nothing keeps those two pointers in sync - if they drift, this
# app ends up with two independently-built copies of the same assembly (e.g. Forge.Primitives.MongoDb)
# on its output path, and .NET silently picks whichever one the build happens to copy last, with no
# warning. This script catches that drift before it becomes a runtime mystery.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
top_level_dir="$repo_root/primitives"
nested_dir="$repo_root/contracts/primitives"

if [ ! -d "$top_level_dir" ] || [ ! -d "$nested_dir" ]; then
    echo "Skipping primitives consistency check: one of $top_level_dir or $nested_dir is missing (submodules not initialized?)."
    exit 0
fi

top_level_commit="$(git -C "$top_level_dir" rev-parse HEAD)"
nested_commit="$(git -C "$nested_dir" rev-parse HEAD)"

if [ "$top_level_commit" != "$nested_commit" ]; then
    echo "primitives submodule drift detected:" >&2
    echo "  primitives/            -> $top_level_commit" >&2
    echo "  contracts/primitives/  -> $nested_commit" >&2
    echo "Bump both to the same commit (git submodule update --remote <path>) before merging." >&2
    exit 1
fi

echo "primitives submodule pointers match ($top_level_commit)."
