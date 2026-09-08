# TfLens — answering "whose gap was it" on the older mistakes

| | |
|---|---|
| Purpose | The steps for filling in the new "whose gap" answer on mistakes recorded before the field existed. |
| Who does it | You. It is a judgement about your own project, and nothing can make it for you. |
| Written | 2026-09-08 |
| How long | About a minute a record once you have the hang of it. You do not have to do all of them. |

---

## 1. What this is actually about

Every recorded mistake has always said **what** went wrong — wrong behaviour, a gap in the
specification, a half-finished implementation. On 7 September the framework added a second column
that says **whose gap it was**, and that is a different and more useful question, because each answer
has a different fix.

The column is called `sort` and it takes exactly one of four answers:

| The answer | What it means | What you do about it |
|---|---|---|
| **The project's own specification did not say it** | The requirement was ours to write and we did not write it | Fix the checklist line. The framework is fine. |
| **The framework never said it anywhere** | No rule existed, in any project | Add one rule to the framework, plus a check |
| **There was a check and it did not catch it** | A rule existed, a gate ran, and it passed anyway | Fix the check. Do not rewrite the prose. |
| **It was written down and ignored anyway** | The rule existed and was not followed | Make it a script or a hook, or delete the rule |

The reason it matters: across the framework's own mistakes, **about half came out as "there was a
check and it did not catch it."** That says the dominant problem is not missing rules but weak checks,
which is a completely different remedy from the one the old column implies. You cannot see that
distribution until some records carry the answer.

## 2. Why TfLens cannot do this for you

Two separate reasons, and both are deliberate:

1. **TfLens never writes to a log file.** It is a read-only lens. It has no code path that appends to
   any stream, and that is one of its founding rules, not an oversight.
2. **The answer is a judgement, not a lookup.** Deciding whether a check was too weak or a rule was
   ignored means knowing what you intended at the time. Nothing in the record says it.

So the answer is added by the framework's own command, run by you, in the repository the mistake
belongs to.

## 3. The command

One line per record:

```bash
bash .tfcore/utils/tf-emit.sh --amend <the mistake's id> sort <one of the four words>
```

The four words the command accepts are exactly:

```
spec          the project's own specification did not say it
unsaid        the framework never said it anywhere
weak-check    there was a check and it did not catch it
ignored       it was written down and ignored anyway
```

A real example, using the most recent mistake in this repository:

```bash
bash .tfcore/utils/tf-emit.sh --amend MISS-TfLens-20260902-10 sort spec
```

**It is safe.** The command refuses a word outside those four, refuses a field that is not meant to be
filled in later, and **never overwrites an answer that is already there** — it only fills a blank. It
adds a new line to the log rather than editing the old one, so nothing is ever rewritten and you can
see afterwards exactly what was answered and when.

## 4. How to decide, in order

Ask these four questions **in this order** and stop at the first "yes". The order is what makes the
answers consistent between one session and the next.

1. **Did our own specification say it clearly?** If no → `spec`.
2. **Did the framework say it anywhere at all?** If no → `unsaid`.
3. **Was there a check, and did it fail to catch this?** If yes → `weak-check`.
4. **Was it written down and ignored anyway?** If yes → `ignored`.

If you genuinely cannot tell, **leave it blank**. A blank is honest and is reported as "written before
the question was asked". A guess is worse than nothing, because it goes into a distribution that
someone will later act on.

## 5. Which ones to do

**Not all of them.** There are 86 unanswered mistakes in this repository and 286 across all your
projects. Answering all of them would take a day and the oldest ones are the hardest to remember.

Suggested order:

| Do these | Why |
|---|---|
| The 22 from September in this repository | Recent enough to remember, and they are the ones the dashboard will show first |
| Then the 64 from August, if you want | Diminishing returns; the picture rarely changes after the first twenty or thirty |
| The other projects | Only if you want a combined figure across projects |

**Twenty answered records is enough to see the shape.** The page states its own denominator — it will
read `22 of 86 sorted` and say the rest predate the field — so a partial answer is a perfectly good
state to stop in and is never presented as if it were complete.

## 6. Seeing what is left

To list the unanswered mistakes in a repository, newest first:

```bash
python3 - <<'EOF'
import json, os
p = os.path.join('docs', 'metrics', 'misses.jsonl')
rows = [json.loads(l) for l in open(p, encoding='utf-8') if l.strip()]
seen = {r['miss_id'] for r in rows if r.get('kind') == 'miss-amend' and r.get('field') == 'sort'}
todo = [r for r in rows if r.get('kind') == 'miss' and not r.get('sort') and r['miss_id'] not in seen]
todo.sort(key=lambda r: r.get('ts', ''), reverse=True)
print(f'{len(todo)} still unanswered\n')
for r in todo:
    print(f"{r.get('ts','')[:10]}  {r['miss_id']:30s}  {r.get('miss_class','')}")
    print(f"    {(r.get('what') or '(no description — this one predates the description field too)')[:110]}")
EOF
```

Run it again after a batch and the list shrinks. It already excludes anything you have answered.

## 7. What happens once some are answered

Nothing is needed from you beyond the answers themselves.

- The framework rebuilds `docs/TfLens-Misses.md` and its HTML after each amendment, so the readable
  list stays current on its own.
- TfLens picks the answers up on its next sync and shows them on the Misses page — **once the work for
  it is built.** That is `REQ-UI-052`, `REQ-FN-106` and `REQ-FN-107`, which are not built yet. The
  page shows the distribution in words beside the existing one, with `n of N sorted` on its face.
- Until then the answers sit safely in the log, losing nothing.

So the order does not matter: answer some now and the page will show them when it ships, or build the
page first and answer afterwards. Neither blocks the other.
