# Evidence log

Every entry names the commit, the environment and the kind of evidence.
Deterministic evidence proves rule consistency only; it does not establish real
tracking, comfort or frame timing (Specification, Section 16). Never record an
Editor or simulator run as a headset result.

## Gate status

| Gate | Deterministic (.NET / EditMode) | Editor / XR Simulator | Hardware (Quest 3) |
| --- | --- | --- | --- |
| A Setup | Keeper boarding starts play exactly once (tests) | **Not run** — needs Unity 6000.3.28f1 on a developer machine | Not run |
| B Carry and hop | 40 scripted hops, all three dolls, both directions; no loss, duplicate owner or leaked slot | Not run | Not run |
| C Chapter logic | Both rescue orders and the replay solution finish; tutorial, pause, queued R closure and every checkpoint stay consistent (with weather) | Not run | Not run |
| D Hardware | — | — | Not run (no headset available) |
| E Experience | — | — | Not run |
| F Release | — | — | Not run |

## Entries

### T02–T03 — palm platforms, palm-to-start, side-by-side hops

- Environment: Linux cloud container, .NET SDK 8.0.427, `Handover.Core` built as
  `netstandard2.1` / C# 9 with warnings as errors; NUnit 3.14.
- Command: `./tools/run-core-tests.sh`
- Result: 51 passed, 0 failed.
- Covers: palm basis handedness for both hands (palm up and down), filter
  frame-rate independence, 1.5 cm lag bound and cling, carry 35/45° and tilt
  25/20° hysteresis without chatter, shield dwell, stale samples, origin reset;
  palm-to-start from either hand, cancelled and withdrawn attempts, duplicate
  commit callbacks, Keeper-only boarding in Ready, Keeper priority, Replay and
  Resume, one minute of slow and abrupt carrying; 40 scripted hops, full
  receiver, stale pose, origin mismatch, no seam outside dry areas, normal
  limit, moving pair, cancel before take-off, separation before and after the
  midpoint, receiver turned over, unload during a hop, deposit and re-board,
  named Departure berths, full dock cue, cooldown and re-arm.
- Design change found by testing: a hand held still next to a dock or the other
  palm would ping-pong the doll back once the 0.5 s cooldown expired. Added a
  re-arm rule (the pair must separate beyond the 7 cm disconnect gap before the
  same transfer is offered again).
- Not verified: anything involving Unity, the XR Simulator, real hand tracking
  or device performance.

### T04 — recovery, pause, checkpoints and saves

- Environment and command as above. Result: 106 passed, 0 failed.
- Covers: hand disappearing during candidate dwell, anticipation, before and
  after the hop midpoint, while carrying, as the source before the midpoint, and
  while boarding; the 0.15 s grace, the stationary system hold, blend-back after
  0.25 s of stable tracking within 5 cm in the same origin epoch, confirmation
  when the hand returns far away; recenter while carrying and mid-hop; pause
  while holding (doll secured, gameplay clock frozen, hands still tracked and
  filtered for UI), pause before and after the midpoint, focus loss and headset
  removal (no auto-resume); a lost shielding hand enters recovery instead of
  failure; tracking recovery never increments the failure counter. Checkpoint
  capture normalizes palm, transfer and hold owners, resolves berth conflicts,
  restores complete snapshots (also mid-hop), rejects 17 kinds of illegal
  snapshot and falls back to the previous valid one; restart and reposition.
  Saves: stable JSON round trip, atomic replace with backup, interrupted write,
  corrupt save to backup to defaults with a message, unknown ids and illegal
  values rejected, illegal checkpoint dropped while settings survive, real
  file-system round trip.
- Mutation check: disabling the origin-epoch check, the 5 cm blend limit, the
  0.15 s grace, or pause's system hold each makes at least one test fail.
- Interpretation recorded: "restore the logical checkpoint after confirmation"
  returns each held passenger to its last safe berth (else its own Departure
  berth) rather than rewinding the whole chapter, so tracking loss never costs
  progress (Section 2: "Lost tracking never costs progress").

### T05 — tabletop stage fitting

- Result: 121 passed, 0 failed (`./tools/run-core-tests.sh`).
- Covers: permission declined and no table fall back to a virtual tabletop that
  passes validation; small, distant, too high and too low tables are rejected
  with a reason; a typical desk fits the Standard layout inside its edges with a
  4 cm margin; a smaller table uses the Compact variant; a rotated table aligns
  the stage with the edge facing the player; the best of several surfaces is
  chosen; every dock is within seated reach and the 58 x 54 degree budget; a
  stage under the eyes or beyond reach fails validation; resize stays within the
  variant's tested limits; recenter drift beyond 3 cm or 4 degrees requires
  repositioning; a fitted layout runs the same chapter rules.
- Not verified: real MRUK scene data, actual seated reach and the real view.
  Reach uses head distance as a proxy for shoulder reach.

### T06–T07 — chapter rules, flood and shelter

- Result: 149 passed, 0 failed (`./tools/run-core-tests.sh`).
- T06 covers, on the real Standard layout and rules: the reference route (Keeper
  to U, Pip via R, Luma direct, Keeper last), the Luma-first route and the replay
  solution without R (0 stops); U locked until the teaching hop; Keeper-only
  boarding until then; the harbour dark until Keeper holds the lantern; lifting
  Keeper early dims the harbour while rescued dolls stay; Keeper comes home last;
  only Keeper may hold the lantern; rescued dolls stay home; duplicate rescue and
  completion callbacks change nothing; Replay resets objectives but keeps settings
  and placement. Five valid checkpoints per run (start, lantern, two rescues,
  completion) and the phase sequence Calm, Low, Rising, Last crossing.
- T07 covers: the phase table (calm before the lantern and on the last crossing;
  R floods in phase 1; D, U, H stay dry); an unsheltered crossing cues shelter at
  0.5 s and restores the complete checkpoint at 2 s; the palm-down canopy shelters
  only when it intercepts the angled rain ray; brief shelter pauses exposure while
  0.3 s of safety clears it; dry-area handovers need no shield; pause freezes the
  hazard clock; a shielding-hand dropout enters recovery and the weather cannot
  punish it; R never closes while occupied or reserved, rejects new arrivals
  while the closure is queued, and the queued condition restores exactly; relaxed
  mode keeps R open with no penalties (and leaving it re-queues the closure
  consistently); shelter assist removes penalties but keeps the route puzzle; the
  whole chapter is solvable with weather, including a shielded direct crossing.
- Mutation check: letting R close while busy, lighting the harbour without the
  lantern, disabling the canopy, or removing commit de-duplication each makes
  two to four tests fail.
- Not verified: whether a real palm-down hand beside the carrier is readable and
  free of occlusion (Section 19 risk). Shelter assist is the planned fallback.

### T08 (engine-free part) — cue ownership and presentation state

- Result: 158 passed, 0 failed.
- Covers: a tilt threshold toggling every frame produces at most one warning per
  second; tracking recovery outranks and briefly ducks tilt warnings; warning
  and rain loops stop while paused or recovering; menu feedback outranks
  gameplay cues; the harbour melody plays one phrase per rescue (1, 2, 3) and
  the completion melody once over a full route. Presentation: Waiting, an
  immediate lean when a palm is offered (then more as dwell builds),
  Anticipating, Hopping with arc progress, Riding at the palm support,
  Sheltering (crouch) on tilt without sliding, Suspended in the system hold,
  Rescued at the harbour.
- Not verified: how the cues sound and whether the animation reads well; that
  needs the Unity presenters and people.
