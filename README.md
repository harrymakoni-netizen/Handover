<p align="center"><img src="Docs/images/handover-logo.png" alt="Handover" width="520"></p>

A tiny wooden passenger steps onto your real palm. You carry them over a flooded
tabletop, steady your other hand alongside, and watch them gather the courage to
hop across. Your hands are the moving dry ground that brings a small community
home.

Handover is a seated mixed-reality rescue chapter for Meta Quest 3, built in
Unity 6 with the Meta Interaction SDK and MRUK, for the Meta VR Start Developer
Competition 2026 (Gaming / New Experience). The locked design is in
[`Docs/Specification.md`](Docs/Specification.md).

## Status

Build plan in progress — nothing here claims a finished game or a passed
hardware gate. See [`Docs/EvidenceLog.md`](Docs/EvidenceLog.md) for what has
actually been verified, and how.

## Layout

| Path | Contents |
| --- | --- |
| `Assets/Handover/Runtime/Core` | Deterministic rescue simulation (no UnityEngine references). |
| `Assets/Handover/Runtime/Unity` | Composition root, hand sources (keyboard, transforms), greybox views, audio, hand menu. |
| `Assets/Handover/Runtime/Meta` | Meta adapters: Interaction SDK hands, OVRManager lifecycle, MRUK tables. |
| `Assets/Handover/Editor` | Scene builders: Meta XR Bootstrap scene and a keyboard greybox scene. |
| `Assets/Handover/Tests/Core` | NUnit tests for the simulation (run in Unity and with `dotnet test`). |
| `tools/` | Engine-free test harness and a Unity compile check that needs no Editor licence. |
| `Docs/` | Specification, setup guide, versions, evidence log, remote test sheet. |
| `CLAUDE.md` | Instructions for Claude Code sessions in this repository. |

## Getting started

Follow [`Docs/Setup.md`](Docs/Setup.md). In short:

1. Install Unity **6000.3.28f1** with Android Build Support (see
   [`Docs/Versions.md`](Docs/Versions.md)).
2. In Unity Hub choose **Add project from disk** and select this folder. The
   first open resolves the pinned packages and writes `Packages/packages-lock.json`
   and the default `ProjectSettings` — commit them.
3. **Handover > Create Greybox Scene** and press Play to try the chapter with
   keyboard palms, or **Handover > Create Meta XR Bootstrap Scene** for Quest and
   the XR Simulator.
4. Run the deterministic tests: *Window > General > Test Runner > EditMode*.

Without Unity, the simulation and its tests run on any machine with the .NET 8 SDK:

```bash
./tools/run-core-tests.sh
```
