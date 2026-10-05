---
name: work-issue
description: End-to-end workflow for a GitHub issue — triage, architect writes spec + plan, human approves, implementer executes, reviewers check the diff, PR opens. Use when the user says "/work-issue NN", "work on issue NN", "pick up #NN", or wants an issue taken from open to PR. Handles the trivial-fix short-circuit (no spec needed).
---

# work-issue

Orchestrates the repo's issue-driven workflow. You are the coordinator: the design thinking belongs to the `architect` agent, the code to the `implementer` agent, the checking to the reviewer agents. Don't do their jobs inline — delegate, relay results, and hold the approval gate.

## 1. Triage

```bash
gh issue view <NN> --comments
```

Classify:

- **Trivial** — typo, doc fix, one-liner with an obvious correct answer, mechanical rename. Still branch (step 2), then skip the spec + approval (steps 3–4) and implement directly.
- **Bindings-only** — a `MiniaudioVersion` bump, an rsp or `MiniaudioDefines` change with no wrapper decision in it. Branch, then use the `regen-bindings` skill instead of steps 3–5; still review (step 6) and open the PR (step 7).
- **Non-trivial** — new API surface, lifetime/ownership or threading semantics, a new `ma_*` type the wrapper allocates, per-frame path changes, a new RID, anything where a reviewer would want the *why* written down. Full flow.

If the issue is unclear or seems to bundle several independent designs, say so to the user before spending architect time.

## 2. Branch

Work never lands on `main` directly:

```bash
git checkout -b issue-<NN>-<short-topic> main
```

## 3. Architect

Launch the `architect` agent with the issue number and any constraints from the user. It returns: the decision, paths to the spec + plan under `docs/design/`, rejected alternatives, and the items it left open.

## 4. Approval gate — hard stop

Relay the architect's decision summary, the alternatives it rejected, and every **Left open** item to the user, plus the two file paths. **Do not proceed to implementation until the user approves.** If they redirect, send the feedback back to the architect for a revision — don't patch the spec yourself.

## 5. Implement

Launch the `implementer` agent pointing at the approved plan (or, for trivial fixes, describing the direct fix). It builds and tests as it goes and returns a report with any deviations.

- Mechanical deviations: fine, keep them for the PR description.
- Design deviations / blocked **Left open** items / a failing `LayoutTests` case: relay to the user; the likely next step is a targeted architect revision, then resume the implementer.

## 6. Review

Run both reviewer agents **in parallel** on the branch diff:

- `miniaudio-correctness-reviewer` — any diff touching `src/Ahjo.Miniaudio/`, `src/Ahjo.Miniaudio.Native/Manual/`, `native/miniaudio/src/ahjo_miniaudio.c`, `native/miniaudio/CMakeLists.txt`, `tools/generate-miniaudio.rsp` or `MiniaudioDefines`.
- `alloc-coverage-checker` — any diff touching per-frame code: the device and engine callbacks, `AudioEngine.Read`, or the control members of `Sound`, `SoundGroup`, `SoundPool` and `AudioListener`.

Real findings go back to the implementer to fix (then re-review the fix). A clean report is a valid result — don't invent work.

## 7. Commit + PR

Commit style: `<area>: <imperative>` (`Wrapper: add sound cones`, `Native: bump miniaudio to 0.11.26`, `CI: add linux-x64 lane`). One logical commit is preferred; use judgment for multi-step plans. Keep a regen in its own commit — the mechanical churn buries the real diff.

```bash
git push -u origin issue-<NN>-<short-topic>
gh pr create --title "<area>: <imperative>" --body "..."
```

PR body: what changed and why (two or three sentences), `Closes #<NN>`, links to the spec + plan files for non-trivial work, test results (including the allocation tests and the AOT publish when they were relevant), and any deviations from the plan. Then report the PR URL to the user.

## Rules

- The approval gate in step 4 is never skipped, compressed into "I'll assume yes", or replaced by a timeout.
- Spec + plan are committed with the implementation — they're part of the change's history.
- If CI fails on the PR, investigate and fix on the branch; don't merge around it. CI runs every RID lane, so a failure on one lane only (a `LayoutTests` case on linux-arm64, say) is a real platform bug, not flakiness.
