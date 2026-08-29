#!/usr/bin/env bash
#
# REQ-NFR-016 — untrack the build output that was committed before 2026-08-28.
#
# WHY THIS EXISTS AS A SCRIPT. `.gitignore` now carries `[Bb]in/` and `[Oo]bj/`, but .gitignore only
# stops NEW files: git keeps tracking anything already in the index, and 1,962 files under bin/ and
# obj/ were committed before that. They have to be removed from the index once.
#
# It matters because `bin/Debug/net10.0/TfLens.staticwebassets.runtime.json` carries ABSOLUTE
# static-web-asset content roots. A WSL build writes /mnt/c/... and /home/<user>/.nuget/...; a
# Windows build rewrites the same file to C:\1MyCode\... and C:\Users\<user>\.nuget\... — both were
# captured on 2026-08-28. Committing that ships one machine's absolute paths to another, which is a
# route to a static web asset resolving on one developer's box and 404ing on the next. That is the
# class of failure behind REQ-UI-001, where /login lost its stylesheet and rendered unstyled with no
# error anywhere.
#
# The agents that produced this repo cannot run it: TechieFlow denies version-control writes to an
# agent in every mode, by design, so history is only ever rewritten by a human who meant to.
#
#   bash scripts/untrack-build-output.sh          # show what would happen; change nothing
#   bash scripts/untrack-build-output.sh --run    # do it
#
# Nothing is deleted from disk. `--cached` removes files from the INDEX only; your build output stays
# exactly where it is and the next build reuses it.
#
# 2026-08-29 — rewritten after the owner ran it and reasonably concluded it had done nothing.
# It had worked: removing 1,962 files from the index STAGES 1,962 deletions, and Visual Studio then
# lists every one of them under "Changes to be committed". That is indistinguishable from "the
# folders are still staged" unless you look at the change type on each row. The script now says so
# in advance, reports the three possible states by name, and verifies the end state itself rather
# than leaving the reader to infer it.

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

PATHS=(
    "src/TfLens/bin" "src/TfLens/obj"
    "src/TfLens.Core/bin" "src/TfLens.Core/obj"
    "tests/TfLens.Core.Tests/bin" "tests/TfLens.Core.Tests/obj"
    "tests/TfLens.Guardrails.Tests/bin" "tests/TfLens.Guardrails.Tests/obj"
    "tests/TfLens.Integration.Tests/bin" "tests/TfLens.Integration.Tests/obj"
)

RM_CMD="git rm -r --cached --quiet -- ${PATHS[*]}"
COMMIT_CMD="git commit -m 'Untrack build output (REQ-NFR-016)'"

# How many build artefacts is git still TRACKING, and how many removals are already STAGED?
tracked()      { git ls-files -- "${PATHS[@]}" | wc -l | tr -d ' '; }
staged_gone()  { git diff --cached --name-only --diff-filter=D -- "${PATHS[@]}" | wc -l | tr -d ' '; }

TRACKED=$(tracked)
STAGED=$(staged_gone)

echo
echo "REQ-NFR-016 — untrack committed build output"
echo "--------------------------------------------"
echo "  still tracked by git : $TRACKED"
echo "  removals already staged : $STAGED"
echo

# ── State C: already done and already committed ──────────────────────────────────────────────────
if [ "$TRACKED" -eq 0 ] && [ "$STAGED" -eq 0 ]; then
    echo "DONE — no build output is tracked and nothing is pending. REQ-NFR-016 is satisfied."
    echo "Re-running this script is safe and will keep saying this."
    exit 0
fi

# ── State B: the removal has been made but not committed ─────────────────────────────────────────
if [ "$TRACKED" -eq 0 ] && [ "$STAGED" -gt 0 ]; then
    cat <<EOF
THE REMOVAL HAS ALREADY WORKED. One step left: commit it.

Git is no longer tracking any build output. The $STAGED entries your editor shows under
"Changes to be committed" are the DELETIONS themselves — that is what success looks like, and it is
easy to misread as "the folders are still staged". Check any row: it is marked as a deletion, not
as an addition or a modification.

Your files on disk were never touched.

Run this to finish:

    $COMMIT_CMD

Then confirm — it should print 0:

    git ls-files | grep -cE '/(bin|obj)/'
EOF
    exit 0
fi

# ── State A: still tracked ───────────────────────────────────────────────────────────────────────
cat <<EOF
$TRACKED build-output files are still in the index.

The command that removes them (your files on disk are untouched):

    $RM_CMD
    $COMMIT_CMD

EOF

if [ "${1:-}" != "--run" ]; then
    cat <<EOF
THIS WAS A DRY RUN. NOTHING HAS CHANGED.

To actually do it:

    bash scripts/untrack-build-output.sh --run
EOF
    exit 0
fi

echo "Running…"
git rm -r --cached --quiet -- "${PATHS[@]}"

AFTER_TRACKED=$(tracked)
AFTER_STAGED=$(staged_gone)

echo
echo "  still tracked by git : $AFTER_TRACKED   (was $TRACKED)"
echo "  removals now staged  : $AFTER_STAGED"
echo

if [ "$AFTER_TRACKED" -ne 0 ]; then
    echo "UNEXPECTED — $AFTER_TRACKED files are still tracked. Nothing was committed; investigate before retrying."
    exit 1
fi

cat <<EOF
IT WORKED. Now read this before looking at your editor:

Visual Studio (and \`git status\`) will now show **$AFTER_STAGED entries under "Changes to be
committed"**. Those are the DELETIONS — the removal from the index — NOT the folders still being
staged. This is the single most confusing moment in the whole change, so: the work is done, and the
long list is the evidence, not the problem.

Nothing was deleted from your disk. The files are still on disk and the next build reuses them.

One step left:

    $COMMIT_CMD

Then confirm — it should print 0:

    git ls-files | grep -cE '/(bin|obj)/'
EOF
