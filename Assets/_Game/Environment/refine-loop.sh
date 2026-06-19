#!/usr/bin/env bash
# Unattended refinement loop for a procedural generator.
# Each iteration: Claude makes ONE change -> harness scores it ->
# keep it (git commit) only if the score went up, else revert.
#
# Run inside a disposable git worktree or container — it gives the agent Bash.
#   git worktree add ../refine-run HEAD && cd ../refine-run && ./refine-loop.sh 80

set -euo pipefail

ITERS=${1:-50}                                   # max iterations
HARNESS_CMD="dotnet run --project Harness -- --seeds 200"
GRADER_PATHS=("Harness" "src/Fitness.cs")        # files the agent must never touch
PLATEAU_LIMIT=12                                 # stop after this many non-improving runs

score()       { eval "$HARNESS_CMD" | grep -oP 'SCORE=\K[-0-9.]+'; }
grader_hash() { find "${GRADER_PATHS[@]}" -type f -print0 | sort -z \
                | xargs -0 sha256sum | sha256sum | cut -d' ' -f1; }
revert()      { git checkout -- . ; git clean -fdq ; }

best=$(score)
echo "$best" > BEST_SCORE.txt
git add -A && git commit -q -m "baseline: $best" || true
echo "baseline SCORE=$best"

stale=0
for i in $(seq 1 "$ITERS"); do
  before=$(grader_hash)

  claude -p "Read CLAUDE.md, BEST_SCORE.txt and JOURNAL.md. Make ONE improvement
to the generator under src/ to raise the score. Do not touch the grader. Run the
harness to verify, then append your result to JOURNAL.md." \
    --allowedTools "Read,Edit,Bash" \
    --permission-mode acceptEdits \
    --bare > /dev/null

  # Goodhart guard: agent must not have edited the grader
  if [[ "$(grader_hash)" != "$before" ]]; then
    echo "iter $i: grader changed -> reverting"; revert; continue
  fi

  # safety net: never accept a change that breaks the regression test
  if ! dotnet test -q >/dev/null 2>&1; then
    echo "iter $i: tests red -> reverting"; revert; continue
  fi

  new=$(score)
  if (( $(echo "$new > $best" | bc -l) )); then
    best=$new; stale=0
    echo "$best" > BEST_SCORE.txt
    git add -A && git commit -q -m "iter $i: improved to $best"
    echo "iter $i: SCORE=$new  kept"
  else
    revert; stale=$((stale+1))
    echo "iter $i: SCORE=$new  reverted (best=$best, stale=$stale)"
  fi

  (( stale >= PLATEAU_LIMIT )) && { echo "plateaued -> stopping"; break; }
done

echo "done. best SCORE=$best"
