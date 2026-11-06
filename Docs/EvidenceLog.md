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
| C Chapter logic | T04 recovery, pause and checkpoints pass; chapter rules pending (T05–T07) | Not run | Not run |
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
