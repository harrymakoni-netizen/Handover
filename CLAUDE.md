# Handover — project instructions for Claude Code

The full design is in `Docs/Specification.md` (converted from
`Docs/Handover_Full_Development_Document_v3.0.docx`, which stays authoritative).
This file is Section 18 of that specification plus the repository facts an
assistant needs before editing anything.

## Project objective

Implement Handover Version 3.0 as specified, using Claude Code as the primary
coding assistant. Three peg dolls use stable palms and adjacent-hand hops to
reach a tabletop harbour. Keeper's boarding starts play; a teaching hop unlocks
the lantern refuge. Rescue the others, then Keeper. Build in simulation first and
record hardware verification separately. Do not propose alternate mechanics or
expand the scope.

## Engineering rules

- Inspect the Unity editor version, package manifest, lockfile, active rig and
  installed SDK source before coding.
- Keep SDK types behind one HandPoseProvider; provide synthetic input using the
  same contract.
- Use one doll slot per palm and exactly one owner per doll. Reserve source and
  receiver throughout a hop.
- Use kinematic local-space movement. Do not introduce ragdolls, moving NavMesh
  baking or fluid simulation.
- Recognize palm roles with context, validity, dwell and hysteresis. Carrying
  takes priority over shielding.
- Advance flood phases only after committed rescue events. Never close an
  occupied or reserved refuge.
- Pause simulation and weather on focus loss, invalid origin or unresolved
  tracking recovery.
- Use standard hand UI and palm-to-start. Add no gaze, finger puzzles, bridges,
  train effects or runtime AI.
- Preserve meta files, GUIDs and serialized references. Report Inspector changes
  and avoid unrelated scene edits.

## Task completion contract

Report files and assets changed, tests actually run, remaining setup, and the
next device checks. Compile after each implementation slice. Never claim a
headset test from an Editor run. Commit the last verified milestone. Use official
documentation and installed package definitions rather than guessed API names.

## Repository layout

| Path | What lives there |
| --- | --- |
| `Assets/Handover/Runtime/Core` | `Handover.Core` assembly. Deterministic rescue simulation. **No UnityEngine references** (`noEngineReferences: true`); math is `System.Numerics`. Everything that decides ownership, rules, flood, recovery and saves lives here. |
| `Assets/Handover/Runtime/Unity` | `Handover.Runtime` assembly. MonoBehaviours that feed poses in, step the simulation once per frame and render state (greybox views, audio). Presentation never writes ownership. |
| `Assets/Handover/Runtime/Meta` | `Handover.Platform.Meta` assembly. The only place Meta SDK types appear (Interaction SDK hands, OVRManager lifecycle, MRUK tables). Compiles only when the Meta packages are installed. |
| `Assets/Handover/Editor` | Editor-only tooling (scene builder, project setup helpers). |
| `Assets/Handover/Content` | Authored content (scenes, ScriptableObject definitions, audio, art). |
| `Assets/Handover/Tests/Core` | NUnit EditMode tests for `Handover.Core`. The same files also run outside Unity (see below). |
| `tools/CoreTests` | .NET projects that compile `Handover.Core` as `netstandard2.1` (Unity's API level, C# 9) and run the shared NUnit tests with `dotnet test`. |
| `Docs/` | Specification, versions, evidence log, remote test sheet. |

## Commands

```bash
# Engine-free core: build as netstandard2.1 / C# 9 and run every deterministic test.
./tools/run-core-tests.sh
```

Inside Unity: Window > General > Test Runner > EditMode runs the same tests.

## Conventions the code relies on

- World frame is Unity's: metres, +Y up, left-handed (X right, Z forward).
- A palm pose's rotation uses the *palm frame*: local +Y is the palm normal (the
  side a doll stands on), local +Z points towards the fingertips.
- `HandoverSimulation.Step` is the single ordered update: sample and filter
  palms, resolve candidates, advance transactions, apply committed objectives,
  resolve pending flood events, publish cues. Nothing else commits gameplay.
- Every committed transfer has a unique `TransactionId`; chapter start, rescues
  and flood triggers are de-duplicated by it.
- All numbers in `InteractionTuning` and `ChapterDefinition` are starting values
  from the specification, not measured results.

## Evidence discipline

Record evidence in `Docs/EvidenceLog.md` in three separate columns: deterministic
(.NET or EditMode) evidence, Editor/simulator evidence, and hardware evidence. A
passing `dotnet test` proves rule consistency only; it says nothing about real
tracking, comfort or frame timing.
