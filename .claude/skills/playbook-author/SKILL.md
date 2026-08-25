---
name: playbook-author
description: >-
  Write, extend, debug or review a Dialogporten playbook — a `*.playbook.yaml`
  file compiled by this repo's DSL into a scripted, multi-stage dialog (stages,
  session vars, effects, conditional actions, routers, dice rolls, front channel
  embeds). Use whenever the request involves a playbook YAML file, "playbook
  DSL", a stage/effect/goto/fce/when block, a text-adventure or wizard-style
  demo dialog, or a DSL compile error from `/playbook/create-from-dsl`.
---

# Authoring Dialogporten playbooks

A playbook is one YAML file describing a state machine that renders as a single Dialogporten
dialog. Stages patch the dialog; buttons POST back to `/mutate/{stateId}/{cursor}` to advance.

**The full reference is [`docs/playbook-dsl.md`](../../../docs/playbook-dsl.md).** Read it before
writing anything non-trivial — this file is the working procedure plus a syntax card.

## Procedure

1. **Read a sample first.** Match the closest one to what's being asked:
   | File | Shows |
   |---|---|
   | `sample-complex.playbook.yaml` | Linear flow, transmissions, no state (10 stages) |
   | `sample-cyoa.playbook.yaml` | Pure branching, many endings, no vars (18 stages) |
   | `sample-game.playbook.yaml` | Vars, effects, `when:`, dice, random targets (9 stages) |
   | `sample-dungeon.playbook.yaml` | Everything: routers, `@var()`, shared combat subsystem, `{if:}` blocks (31 stages) |

2. **Sketch the stage graph before writing YAML.** Name every stage, list its exits, and mark
   which stages mutate state. Stage order in the file sets cursor indices — put `start` first.

3. **Declare every variable up front** under `vars:`. Undeclared names are compile errors, and
   this is the most common failure.

4. **Write the file.** Copy the header (`party`, `service-resource`) from a sample unless the user
   supplies their own.

5. **Lint it — always, before reporting done:**
   ```bash
   dotnet run --project .claude/skills/playbook-author/lint -- path/to/x.playbook.yaml
   ```
   This compiles with the server's own `DslCompiler` and reports compile errors plus warnings
   for unreachable stages, dead ends, zero-op stages, over-long `extended-status`, activity
   `description` misuse, priority-cap violations, unused FCEs and non-exhaustive `when:` sets.
   Resolve every warning or explain why it's intentional.

6. **Walk the checklist** in [`docs/playbook-dsl.md` §12](../../../docs/playbook-dsl.md#12-gotchas-checklist).

7. **To run it:** upload at `/playbook/create` in the running app (the page has an environment
   picker), or
   `curl -X POST '{baseUri}/playbook/create-from-dsl?environment=tt02' -H 'Content-Type: text/yaml' --data-binary @file.yaml`
   with a service-owner token (the `altinn-test-token` skill can mint one). `environment` is
   optional (`tt02`, `at23`, `local`; default from configuration) and never part of the YAML.

## Syntax card

```yaml
playbook:
  party: urn:altinn:person:identifier-no:08895699684
  service-resource: urn:altinn:resource:ttd-dialogporten-automated-tests
  language: en
  start: start
  initial: { title: "…", summary: "…" }     # fallback title/summary at dialog creation

  vars:                                      # every name used anywhere must be here
    hp: 10                                   # int | bool | string | list; no floats
    inventory: []
    location: "start"
    _seed: "demo-2026"                       # deterministic RNG (optional)
    _debug: false                            # appends state dump to additional-info (optional)

  stages:                                    # insertion order = cursor 0,1,2…
    start:
      title: "Town square — {vars.hp} HP"    # replace
      summary: "…"                           # replace
      status: InProgress                     # InProgress|Draft|RequiresAttention|Completed|NotApplicable|Awaiting
      extended-status: A quiet morning       # ≤ 25 chars
      additional-info: &statusbar |          # markdown; YAML anchors work well here
        **HP {vars.hp}** · **📍 {vars.location}**
      content: start-fce                     # name of an `fce:` entry → mainContentReference
      effects:                               # run on entry — NOT on the start stage
        - set location = "start"
      transmissions:                         # APPENDS every visit
        - type: Information                  # Information|Acceptance|Rejection|Request|Alert|Decision|Submission|Correction
          from: Inventory                    # omitted/"ServiceOwner" → ServiceOwner, else PartyRepresentative
          title: Iron sword
          summary: "+2 to attack."
          content: sword-fce                 # optional fce → contentReference
      activity:                              # APPENDS every visit
        type: Information                    # `Information` requires description; others must omit it
        description: You pick up the sword.
      actions:                               # max 7: 1 primary + 1 secondary + 5 tertiary
        - Walk to the forest → forest        # shorthand "Label → target" (or "->")
        - label: Fight the wolf
          target: combat
          priority: secondary                # else positional: 1st primary, 2nd secondary, rest tertiary
          when: has_sword                    # expression | "else" | omitted (always shown)

    router:                                  # dispatches on entry without rendering
      goto:
        - { target: vault, when: inventory contains "key" }
        - vault-locked                       # unconditional — a pure router MUST end with one

  fce:
    start-fce: |                             # shorthand = text/markdown
      # The town square
      You carry **{vars.gold} gold**.
    legal:
      media-type: text/html                  # text/markdown | text/plain | text/html
      content: "<p>…</p>"
```

**Targets:** `stage-name` · `next` / `previous` (file order!) · `restart` (cursor 0, *not*
`start:`) · `?(a, b)` or `?(a:3, b:1)` random · `'@var(NAME)'` computed · `$goto=3` raw.

**Effects:** `set X = EXPR` · `inc X [by EXPR]` · `dec X [by EXPR]` · `add X += EXPR` ·
`remove X -= EXPR` · `roll X = 1d6` · `roll X = 1..20` · `if COND then STMT`.

**Expressions:** `|| && !` · `== != < <= > >=` · `+ - * /` (integer) · `LIST contains VALUE`
(bare identifier on the left only) · `"strings"` · `( )`.

**Templating** in title/summary/extended-status/additional-info/action labels/transmission text/
FCE bodies: `{vars.NAME}` · `{if:EXPR}…{else}…{end}` · `{baseUri}` · `{stateId}`.

## The five mistakes that actually happen

1. **Assuming a stage resets the dialog.** Omitted keys inherit from the previous stage. A stage
   with no `actions:` leaves the previous stage's buttons on screen — a dead end.
2. **Putting setup in `effects:` on the start stage.** Bootstrap skips effects and routers
   entirely; the first render only ever sees the literal `vars:` values.
3. **Guarding every action without a `when: else`.** If no condition holds, the stage renders
   with zero buttons and the run is over.
4. **More than 7 actions on a stage**, or two of the same priority. Dialogporten rejects the whole
   PATCH, and the cap counts authored actions, not the ones `when:` leaves visible.
5. **Typos in stage-level keys.** They're silently ignored — no compile error, just a stage that
   quietly does nothing. Lint output (unreachable / dead-end warnings) is usually the first hint.

## Debugging a live playbook

- Set `_debug: true` in `vars:` and give every stage an `additional-info:` — the dialog then
  carries a live dump of cursor, `stateId` and all vars.
- Set `_seed: "something"` to make dice and `?(…)` targets replay identically.
- The server logs each mutate at `Information` level: entry vars, every effect's before → after,
  goto dispatches, each action's `when` result, and the final keep-list.
- `[template error: …]` in the rendered text means a `{if:}` block failed at runtime — those are
  only compile-checked in `title`, `summary`, `extended-status`, `additional-info` and FCE bodies.
- A literal `{vars.foo}` in the output means `foo` isn't declared or is misspelled.
- State lives in memory: restarting the server invalidates every existing playbook dialog.
