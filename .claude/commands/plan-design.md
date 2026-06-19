---
description: Turn the design we just refined into an execution-ready implementation plan
argument-hint: [optional focus or constraint]
---

We've been refining a design together in this conversation. Now turn it into a comprehensive,
hand-off-ready implementation plan. Treat our discussion above as the source of intent.

Enter plan mode, then:

1. Re-read the design discussion above and restate, briefly, what we agreed to build — so we're
   aligned before you plan.
2. Review the parts of the codebase this design actually touches: real files, existing patterns,
   and conventions (follow CLAUDE.md and the relevant module CLAUDE.md / product-design skill).
   The plan must fit how this repo already works, not a generic approach.
3. Before writing the plan, surface every ambiguity, gap, or still-open decision. Ask me about
   anything you'd otherwise have to assume — use AskUserQuestion for the consequential forks.
   Do not proceed to the plan with unresolved guesses.
4. Only once those are resolved, write the plan: file-by-file changes, new/modified types and API
   contracts, tests to add, and the order of work. Another agent should be able to execute it
   without re-deriving your decisions.

If $ARGUMENTS is provided, treat it as an extra focus area or constraint for the plan.

Planning only — do not write code or edit files.
