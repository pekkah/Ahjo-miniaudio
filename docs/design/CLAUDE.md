# docs/design/ — spec-driven design docs

Non-trivial design work produces a paired spec + plan here, named after the GitHub issue that drives it:

- `specs/YYYY-MM-DD-issue-NN-<topic>-design.md` — the design spec: **what and why**
- `plans/YYYY-MM-DD-issue-NN-<topic>.md` — the implementation plan: **how**

The date is the day the spec is written; `NN` is the issue number, zero-padded to two digits; `<topic>` is a short kebab-case slug. The plan's first line links back to its spec (`Paired with [../specs/…](../specs/…)`).

## Quality bar (calibrate against the issue-02 pair)

A spec is evidence-based, not aspirational:

- Opens with the **Issue** link and **Date**, then **Problem** — the defect or gap, with a location for every claim about the code.
- An **Evidence** section, numbered `E1`, `E2`, … so the decisions can refer back to it: what an actual audit showed (call sites counted, consumers listed, layouts diffed). miniaudio's behavior is cited as `miniaudio.h:line` from the pinned header in `native/miniaudio/include/`, not recalled. Claims without locations don't belong. The issue-06 spec's `E1`–`E4` are the model.
- A **Decision** section that names the chosen option, the public API shape, and — where the change has any — the lifetime and threading rules.
- **Why not the alternatives** (or **Rejected**): each rejected option gets a sentence or two on why.
- **Out of scope**, and links to related issues/specs it resolves, prevents, or must land consistently with.
- A behavior the null backend cannot produce in CI (a reroute, a device loss) is called out as untested rather than assumed.

A plan is executable without re-deciding anything:

- Numbered sections, each naming the exact files touched and the exact change (signatures, member names, message shapes — not "update the interface").
- A **layout oracle** step whenever the wrapper starts allocating a `ma_*` type or touching a field miniaudio fills in: the `AHJO_MA_SIZEOF` / `AHJO_MA_OFFSETOF` line and the matching `LayoutTests` entry (CLAUDE.md invariant 3).
- A **tests** step describing the concrete cases to add — on the null backend or a `NoDevice` engine — and, when a per-frame member is added or changed, the line that puts it under an allocation test.
- A **sample and docs** step when public API changes: `samples/HelloAudio` moves to the new API (invariant 7), plus `src/Ahjo.Miniaudio/README.md`.
- A closing **Left open** section for anything the architect deliberately did not decide; the implementer stops and asks rather than improvising design.

A spec may be written after the fact to record what a change turned into once it was built (the issue-06 spec is one). It follows the same structure and says so in its Problem section; it has no plan.
