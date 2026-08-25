# Playbook DSL reference (`*.playbook.yaml`)

A **playbook** is a scripted, multi-step Dialogporten dialog. You write one YAML file describing
a set of **stages**; the server compiles it into a JSON Patch document per stage, creates one
dialog, and applies the first stage. Every button in the dialog is a `write` GUI action that
POSTs back to this service, which applies the next stage's patches to the same dialog.

The result behaves like a small state machine — or a text adventure — rendered entirely through
a normal Dialogporten dialog in Arbeidsflate.

Implementation: [`Playbook/Dsl/DslCompiler.cs`](../Playbook/Dsl/DslCompiler.cs) (YAML → patches),
[`Playbook/Dsl/Script.cs`](../Playbook/Dsl/Script.cs) (expressions, effects, templating),
[`Playbook/PlaybookCompiler.cs`](../Playbook/PlaybookCompiler.cs) (per-render substitution),
[`Controllers/MutateController.cs`](../Controllers/MutateController.cs) (runtime).

Worked examples in the repo root: `sample-complex.playbook.yaml` (linear, no state),
`sample-cyoa.playbook.yaml` (branching), `sample-game.playbook.yaml` (vars, effects, dice),
`sample-dungeon.playbook.yaml` (the full feature set).

---

## 1. Mental model

Everything happens on **one dialog**. Each stage is a set of JSON Patch operations applied on top
of whatever the previous stage left behind:

- `title`, `summary`, `status`, `extended-status`, `additional-info`, `content` **overwrite**.
- `transmissions` and `activities` **append** — revisiting a stage appends them again.
- `actions` replaces the whole GUI action list.
- **A key you omit is not cleared.** It keeps the value the previous stage set.

That last point is the single most important rule. A stage without `title:` shows the previous
stage's title; a stage without `actions:` shows the previous stage's buttons (and is a dead end).

Session state (`vars`) lives server-side, keyed by an opaque `stateId`, and persists across the
whole run — including "restart" jumps back to the first stage.

---

## 2. File shape

```yaml
playbook:
  party: urn:altinn:person:identifier-no:08895699684   # required
  service-resource: urn:altinn:resource:ttd-dialogporten-automated-tests  # required
  language: en          # optional, default "en" — one language for the whole playbook
  start: start          # required — name of the stage rendered first

  initial:              # optional; the dialog's title/summary at creation time
    title: "The Catacomb Below"
    summary: "A dungeon crawler demo."

  vars: {}              # optional — declared session variables
  stages: {}            # required — at least one
  fce: {}               # optional — named front channel embed bodies
```

**Key naming:** hyphenated, mapped to the C# model by
`HyphenatedNamingConvention` — `service-resource`, `extended-status`, `additional-info`,
`media-type`.

**`initial` is a fallback.** The dialog is created with these, then the start stage's patches are
applied immediately in the same flow. It only shows through if the start stage omits `title:` /
`summary:`. Defaults are `"Playbook"` / `"Playbook dialog"`. The dialog is always tagged with the
search tag `Playbook`.

> ⚠️ **Unknown keys are silently ignored** at the top level, on stages, and inside `transmissions`
> entries (`IgnoreUnmatchedProperties`). A typo like `additionalinfo:` or `aditional-info:` does
> not fail the compile — it just does nothing. Unknown keys inside an **`activity`, `action`,
> `goto` or `fce` object** *are* hard errors.

---

## 3. Stages

`stages:` is a YAML **mapping**, and its insertion order defines each stage's **cursor index**
(0, 1, 2, …). Order matters for the `next`, `previous` and `restart` targets, and nothing else.

```yaml
stages:
  entry:
    title: The entrance hall
    summary: Cold air from the north.
    status: InProgress
    extended-status: Entrance hall      # ≤ 25 chars
    additional-info: |
      **HP {vars.hp}/{vars.max_hp}** · **{vars.gold}g**
    content: entry                      # name of an `fce:` entry
    effects:
      - set location = "entry"
    transmissions: []
    activities: []
    actions:
      - Go north to the hall → hall
```

| Key | Type | Compiles to | Notes |
|---|---|---|---|
| `title` | string | `replace /content/title/value/0/value` | Plain text. |
| `summary` | string | `replace /content/summary/value/0/value` | Plain text. |
| `status` | enum | `replace /status` | See [§10](#10-enum-reference). |
| `extended-status` | string | `add /content/extendedStatus` | `text/plain`, ≤ 25 chars. |
| `additional-info` | string | `add /content/additionalInfo` | `text/markdown`. |
| `content` | fce name | `add /content/mainContentReference` | Front channel embed, see [§8](#8-front-channel-embeds-fce). |
| `transmissions` | list | one `add /transmissions/-` each | **Appends.** |
| `activity` / `activities` | object/list | one `add /activities/-` each | **Appends.** |
| `actions` | list | `add /guiActions` | Replaces the whole array. |
| `effects` | list of strings | *(not a patch)* | Runs on stage entry, see [§6](#6-effects). |
| `goto` | list | *(not a patch)* | Router rules, see [§7](#7-router-stages-goto). |

### Transmissions

```yaml
transmissions:
  - type: Information        # required, see §10
    from: Inventory          # omitted or "ServiceOwner" → actorType ServiceOwner;
                             # anything else → PartyRepresentative with this actorName
    title: Iron sword        # text/plain
    summary: "+2 to attack." # text/plain
    content: sword-detail    # optional fce name → contentReference
```

### Activities

Shorthand (type only) or full object:

```yaml
activity: DialogClosed

activity:
  type: Information
  description: You strap the sword across your back.
  by: ServiceOwner           # same actor rules as transmission `from`

activities:                  # both keys may be used; `activity` is emitted first
  - DialogOpened
  - { type: Information, description: "…" }
```

> Activity type `Information` **requires** `description`; every other type must **not** have one.

---

## 4. Actions

Two forms. Shorthand covers the common case:

```yaml
actions:
  - Go north to the hall → hall          # "Label → target"; ASCII "->" also works
  - label: Open the great chest          # object form
    target: open-chest
    priority: secondary                  # optional
    when: '!(looted contains "vault")'   # optional
```

Every action compiles to the same GUI action shape — `action: write`, `httpMethod: POST`,
`url: {baseUri}/mutate/{stateId}/<target-cursor>` — so clicking it drives the state machine.

### Priority

If you don't set `priority:`, it is assigned **positionally**: the first action is `primary`,
the second `secondary`, the rest `tertiary`.

Dialogporten enforces **at most 1 primary, 1 secondary and 5 tertiary** GUI actions, so a stage
may declare **at most 7 actions**. Because priorities are assigned before `when:` filtering, the
cap applies to the *authored* count, not the rendered count — eight `when`-guarded actions of
which only three ever show still fail the PATCH with
`Only five tertiary GUI actions are allowed.` Split the stage or gate choices behind a router.

Priorities are also assigned at **compile** time, before `when:` filtering, so a stage whose first
action is filtered out simply renders with no primary button — harmless, but worth knowing.

### Targets

| Target | Meaning |
|---|---|
| `some-stage` | Jump to that stage. Unknown names are a compile error. |
| `next` | Cursor + 1 — the **next stage in file order**. |
| `previous` | Cursor − 1. |
| `restart` | Cursor 0 — the **first stage in the file**, which is not necessarily `start:`. |
| `?(a, b, c)` | Uniform random pick, resolved when the patch is compiled. |
| `?(a:3, b:1)` | Weighted random pick (positive integer weights). |
| `@var(NAME)` | The stage named by session var `NAME`, resolved when the button is clicked. |
| `$…` | Raw command passthrough: `$next`, `$previous`, `$goto=3`, `$random=2:3\|5:1`, `$gotovar=NAME`, `$gotoIfProgress=1-0-2`. |

`@var(NAME)` is how shared subsystems return the player to where they came from — set a var to the
current stage name on entry to each room, then target `'@var(location)'` from the shared stage:

```yaml
entry:
  effects: [set location = "entry"]
  # …
combat-victory:
  actions:
    - label: Catch your breath and look around
      target: '@var(location)'
```

If `NAME` is undefined, holds a non-string, or holds something that isn't a stage name, the
mutate request fails with 400.

> ⚠️ Don't put an `@var(…)` target on the **start** stage. The Blazor upload page
> (`Components/Playbook/CreatePlaybook.razor`) bootstraps without a stage-cursor map, so resolving
> one there throws; the `POST /playbook/create-from-dsl` endpoint does supply the map and works.

### `when:` — conditional actions

- **absent** → always rendered.
- **an expression** → rendered iff it evaluates to `true`.
- **the literal `else`** → rendered iff *no expression-guarded action in the same stage matched*.
  Actions with no `when` do not count as a match.

```yaml
actions:
  - label: Climb out with the Crystal
    target: escape-ending
    when: inventory contains "crystal"
  - label: Give up and crawl back to daylight
    target: abandon-ending
    when: else
```

An expression that throws at evaluation time drops that action (logged as a warning) and does not
count as a match.

> ⚠️ If **every** action in a stage has a `when:` and none is `when: else`, the stage can render
> with **no buttons at all** — a hard dead end. Either prove the conditions are exhaustive or add
> a `when: else` escape hatch.

---

## 5. Variables

```yaml
vars:
  hp: 10
  max_hp: 10
  has_sword: false
  current_monster_name: ""
  inventory: []
  location: "entry"
  _seed: "demo-2026"
  _debug: false
```

**Every variable used anywhere must be declared here** — in effects, `when:` expressions, `goto`
conditions, `@var()` targets and `{if:}` template blocks. An undeclared name is a compile error:

```
undeclared variable 'gold' in 'inc gold by 5' (declare it under playbook.vars)
```

### Types

Four types: **int** (64-bit), **bool**, **string**, and **list** of those. No floats, no maps.
YAML scalars are coerced: `true`/`false` → bool, `null`/`~` → null, integer-looking → int,
everything else → string. Quote a value you want to stay a string (`"0"`, `"true"`).

### Reserved variables

| Var | Type | Effect |
|---|---|---|
| `_seed` | string | Makes randomness deterministic. The RNG is seeded from `MD5("<_seed>\|<stateId>")`, so one playbook replays identically while two dialogs from the same file still diverge. Governs `roll` and `?(…)` targets. |
| `_debug` | bool | Appends a `**[debug]**` block (cursor, stateId, every var) to `/content/additionalInfo`. **Only works on stages that set `additional-info:`** — there is no patch to append to otherwise — and never on the start stage, since the block is added by the mutate endpoint. |

Both are ordinary declared vars, so you can also read them in expressions.

### Lifetime

Vars are stored per `stateId` in an **in-memory** store. They survive every stage transition,
including `restart` — a "Try again" button re-renders stage 0 but does **not** reset the vars.
Restarting the server drops every playbook and its state.

---

## 6. Effects

`effects:` is an ordered list of statement strings, applied to the session vars **on stage entry**,
before the stage renders and before its `goto` rules are evaluated.

```yaml
effects:
  - set weapon_bonus = 2
  - inc gold by 25
  - dec hp by monster_dmg
  - add inventory += "key"
  - remove inventory -= "torch"
  - roll player_base = 1d6
  - roll treasure = 1..20
  - if hp <= 0 then set hp = 1
```

| Statement | Meaning |
|---|---|
| `set X = EXPR` | Assign. |
| `inc X [by EXPR]` | Add (default 1). Int only. |
| `dec X [by EXPR]` | Subtract (default 1). Int only. |
| `add X += EXPR` | Append to a list. Duplicates are allowed — guard with `contains` if you need set semantics. |
| `remove X -= EXPR` | Remove **all** equal elements from a list. |
| `roll X = NdM` | Sum of N dice with M sides. N ≥ 1, M ≥ 2. |
| `roll X = LO..HI` | Inclusive integer range. |
| `if COND then STMT` | Guards a **single** statement. No `else`. Nest with `if a then if b then …`. |

Rules and gotchas:

- **Effects do not run for the start stage.** Bootstrap only compiles and applies the start stage's
  patches; the effect/router loop lives in the mutate endpoint. The first screen sees exactly the
  values declared in `vars:`. Put your setup on a stage the player navigates *to*.
- Statements run **in order**, each seeing the previous one's result — `roll player_base = 1d6`
  then `set player_dmg = player_base + weapon_bonus` works.
- Any failure (type error, division by zero) aborts the whole mutate with 400 and nothing renders.
- When a `goto` chain walks through several stages, **every** stage's effects in the chain run.

---

## 7. Router stages (`goto`)

A stage with `goto:` dispatches somewhere else on entry — after its own effects run — without
rendering. Rules are evaluated in order and the first match wins.

```yaml
# vault-door never renders: it dispatches based on whether the player carries the key.
vault-door:
  goto:
    - target: vault
      when: inventory contains "key"
    - target: vault-locked        # bare string / no `when:` = unconditional fallback
```

- Entries are either an object `{target, when}` or a bare string target (unconditional).
- Targets may be a stage name or `@var(NAME)`.
- A rule placed **after** an unconditional rule is a compile error (unreachable).
- If no rule matches, the stage **falls through and renders itself**.
- Chains are capped at **16 hops**; longer means 400 (cycle protection).

> ⚠️ A pure router — only `goto:`, no content keys — compiles to **zero patch operations**. If it
> ever falls through, the mutate request fails with 400 because there is nothing to patch. Always
> end a pure router with an unconditional rule.

---

## 8. Front channel embeds (`fce`)

An FCE is the rich body rendered inside the dialog. Define them by name, reference them from a
stage's `content:` (main content) or a transmission's `content:`.

```yaml
fce:
  entry: |                          # shorthand: a markdown string
    # The entrance hall
    You carry **{vars.gold} gold**.

  legal-text:                       # object form
    media-type: text/html
    content: "<p>Some HTML.</p>"
```

- Allowed media types: `text/markdown` (default), `text/plain`, `text/html`. Anything else is a
  compile error.
- Every referenced name must be defined (compile error otherwise). Defined-but-unused is allowed.
- Served from `{baseUri}/fce/named/{stateId}/{name}`, authenticated with the dialog token, under
  `X-Content-Type-Options: nosniff` and a strict CSP (`sandbox; default-src 'none';
  style-src 'unsafe-inline'`) — so `text/html` bodies get **no scripts and no external assets**.
- The body is templated **at iframe load time** against the current session vars, so it always
  reflects live state.
- Every compiled FCE URL gets a random `_cb=…` query parameter, because Arbeidsflate only reloads
  an iframe when its URL changes and the same named FCE can be re-referenced by a later stage.

YAML anchors are a good fit for a status bar shared by every stage:

```yaml
    start:
      additional-info: &statusbar |
        **HP {vars.hp}/{vars.max_hp}** · **{vars.gold}g** · **📍 {vars.location}**
    entry:
      additional-info: *statusbar
```

---

## 9. Expressions and templating

### 9.1 Expression language

Used by `when:`, `goto … when:`, `if … then` and `{if:…}` blocks.

Precedence, loosest first: `||` → `&&` → `!` → comparison → `+` `-` → `*` `/` → atom.

```
gold >= 5
hp > 0 && monster_hp > 0
!has_sword
!(looted contains "vault")
inventory contains "crystal"
(xp / 10) + weapon_bonus > 3
```

- Atoms: integer literal, `true`/`false`, `"double-quoted string"` (escapes `\n \t \\ \"`),
  a variable name, `(…)`, and `identifier contains EXPR`.
- `contains` only accepts a **bare identifier** on the left — `(a) contains x` does not parse.
- Arithmetic is integer only; `/` truncates; dividing by zero is an error.
- `<` `<=` `>` `>=` accept ints and bools (bool → 1/0). Comparing strings with them is an error.
- `==` / `!=` compare ints, bools, strings and null.
- `!` requires a bool. Comparisons are non-associative — `a < b < c` does not parse.
- `!` binds looser than comparison, so `!a == b` means `!(a == b)`. Parenthesise when in doubt.

### 9.2 Templating in author-visible text

| Construct | Meaning |
|---|---|
| `{vars.NAME}` | The variable's display form. An unknown name is left **literally** in the output — that's your typo signal. |
| `{if:EXPR}…{else}…{end}` | Conditional block. Nestable; `{else}` optional. A malformed block renders `[template error: …]` inline. |
| `{baseUri}`, `{stateId}` | The service base URI and this run's state id. |

Display forms: bool → `true`/`false`, int → decimal, null → empty string, list → comma-separated
(`"key, amulet"`).

**Where templating is applied:** every string in a stage's compiled patch — `title`, `summary`,
`extended-status`, `additional-info`, transmission `title`/`summary`, activity `description` and
**action labels** — plus FCE bodies.

**Where `{if:}` is compile-checked:** only stage `title`, `summary`, `extended-status`,
`additional-info`, and FCE bodies. A malformed block in a transmission title, activity description
or action label survives compilation and shows up as `[template error: …]` at runtime.

```yaml
summary: '{if:cleared contains "hall"}The goblin lies where it fell.{else}A goblin blocks the way.{end}'
title: "Battle: {vars.current_monster_name} — {vars.hp}/{vars.max_hp} HP"
```

> **YAML tip:** a scalar starting with `{` is a flow mapping. Quote it (single quotes are easiest,
> since expressions use double quotes for strings) or use a block scalar (`|`, `>-`).

---

## 10. Enum reference

Values accepted by the Dialogporten API client (`Altinn.ApiClients.Dialogporten` 1.103.0). The DSL
passes these through verbatim, so a bad value fails at PATCH time, not compile time.

**`status`** — `InProgress`, `Draft`, `RequiresAttention`, `Completed`, `NotApplicable`,
`Awaiting`. The samples use `Draft` → `InProgress` → `Completed`. The input enum also still accepts
the deprecated `New` and `Sent`, which Dialogporten silently coerces to `NotApplicable` and
`Awaiting` — don't use them. A dialog created without a status defaults to `NotApplicable`.

**Activity `type`** — `DialogCreated`, `DialogClosed`, `Information`, `TransmissionOpened`,
`PaymentMade`, `SignatureProvided`, `DialogOpened`, `DialogDeleted`, `DialogRestored`,
`SentToSigning`, `SentToFormFill`, `SentToSendIn`, `SentToPayment`, `FormSubmitted`, `FormSaved`,
`CorrespondenceOpened`, `CorrespondenceConfirmed`.

**Transmission `type`** — `Information`, `Acceptance`, `Rejection`, `Request`, `Alert`, `Decision`,
`Submission`, `Correction`.

**Action `priority`** — `primary`, `secondary`, `tertiary`.

**Actor** (`from:` on a transmission, `by:` on an activity) — omitted or `ServiceOwner` yields
`actorType: ServiceOwner`; any other value yields `actorType: PartyRepresentative` with that string
as `actorName`.

---

## 11. Runtime pipeline

1. **Upload.** Either the Blazor page `/playbook/create` (file picker + environment picker) or
   `POST /playbook/create-from-dsl` with `Content-Type: text/yaml` and the file as the raw body.
   The **environment** (TT02, AT23, local — see `DialogportenEnvironments` in configuration) is
   chosen here, not in the YAML: the picker on the page, or `?environment=<key>` on the endpoint.
   It is stored with the playbook's server-side state, so every later stage is patched into the
   same environment, and the "open in inbox" link points at that environment's Arbeidsflate.
2. **Compile.** `DslCompiler.Compile` produces a blueprint: one JSON Patch array per stage, plus
   per-stage behavior (effects, action guards, router rules), the initial vars, and the
   stage-name → cursor map.
3. **Create.** A dialog is created from `party`, `service-resource` and `initial`.
4. **Bootstrap.** The start stage's patches are compiled against the *initial* vars and PATCHed.
   Effects and `goto` rules do **not** run here.
5. **Click.** Each GUI action POSTs `{baseUri}/mutate/{stateId}/{cursor}` with the dialog token.
6. **Mutate.** `MutateController` verifies the token's dialog id, applies the stage's effects,
   walks the `goto` chain, persists the vars, compiles the settled stage's patches (resolving
   `$…` commands into mutate URLs, and substituting `{vars.…}` / `{if:…}` / `{baseUri}` /
   `{stateId}`), filters `guiActions` by their `when` guards, optionally appends the `_debug`
   block, and PATCHes the dialog.

`POST /playbook/create` (JSON) is the older, lower-level path: it takes a pre-built patch array
directly instead of YAML.

Dialogporten requires every GUI action `url` to be **valid HTTPS** and ≤ 1023 characters, so
`ServiceProvider:mutateBaseUri` must be an `https://` URL reachable from Arbeidsflate.

---

## 12. Gotchas checklist

Before shipping a playbook, walk this list:

- [ ] **Omitted keys inherit.** Every stage that should change the title/summary/status says so.
- [ ] **No dead ends.** Every renderable stage has `actions:` (or `goto:`), and every stage is
      reachable from `start`.
- [ ] **No stage with zero patch operations.** Pure routers end with an unconditional rule.
- [ ] **`when:` coverage.** No stage where all actions are guarded without a `when: else`.
- [ ] **Effects don't run on the start stage.** Setup lives on a later stage.
- [ ] **Transmissions and activities append.** Don't put them on a stage the player revisits in a
      loop unless duplicates are what you want.
- [ ] **`restart` means cursor 0**, not `start:`. Put your start stage first, or use its name.
- [ ] **`next`/`previous` follow file order**, not narrative order. Prefer explicit stage names.
- [ ] **`restart` does not reset vars.** Reset them yourself with `effects:` on the landing stage.
- [ ] **`extended-status` ≤ 25 characters** (per localized value; enforced by Dialogporten).
- [ ] **Activity `Information` needs a `description`; other types must not have one.**
- [ ] **At most 7 actions per stage** — 1 `primary`, 1 `secondary`, 5 `tertiary`, counted before
      `when:` filtering.
- [ ] **Every variable is declared under `vars:`.**
- [ ] **`_debug` needs `additional-info:`** on the stages you want to inspect.
- [ ] **Quote scalars starting with `{`** and any label containing `: `.
- [ ] **Stage-level key typos are silent.** Re-read your key names against [§3](#3-stages).

Internal limits worth knowing: a stage's patch tree is walked to a depth of 32 and at most 1000
nodes; past that, `{vars.…}` substitution and `$`-command rewriting silently stop. Router chains
are capped at 16 hops. Uploads through the GUI are capped at 10 MB.

---

## 13. Validating a playbook

Compile a file with the server's own compiler, without starting the server:

```bash
dotnet run --project .claude/skills/playbook-author/lint -- sample-dungeon.playbook.yaml
```

It reports compile errors and warns about unreachable stages, dead ends, zero-op stages,
over-long `extended-status`, activity `description` misuse, duplicate priorities, unused FCEs and
exhaustive-`when` risks. See [`.claude/skills/playbook-author/`](../.claude/skills/playbook-author/)
for the accompanying authoring skill.
