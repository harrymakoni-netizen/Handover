# Setting up and running Handover

This is the T01 checklist for a developer machine. Everything here needs the Unity
Editor, which the cloud session that wrote the code did not have; each step lists what to
check. Record results in [`EvidenceLog.md`](EvidenceLog.md).

## 1. Install

1. Unity Hub, then **Unity 6000.6.8f1** (the version the project now uses) with
   *Android Build Support* (+ OpenJDK and Android SDK & NDK Tools). See
   [`Versions.md`](Versions.md).
2. Meta XR Simulator (Windows has full support; macOS hand tracking is limited):
   Meta Quest Developer Hub > Tools, or `metavr tools install xrsim`.

## 2. First open

1. Unity Hub > **Add project from disk** > this repository folder > open.
2. Package Manager resolves the pinned packages (Meta's scoped registry
   `npm.developer.oculus.com` and Unity's registry). Accept the Input System
   "enable backends / restart" prompt if it appears.
3. Expect Unity to create `Packages/packages-lock.json`, the default
   `ProjectSettings/*.asset` files and the `.meta` files it still needs. **Commit them**
   (Section 10: commit the manifest *and* the lockfile).
4. Check: the Console has no compile errors. The four Handover assemblies are
   `Handover.Core`, `Handover.Runtime`, `Handover.Platform.Meta` (only compiles when the
   Interaction SDK 207+ is installed) and the Editor assemblies.

## 3. Render pipeline and platform

1. *Assets > Create > Rendering > URP Asset (with Universal Renderer)*, then assign it in
   *Project Settings > Graphics > Default Render Pipeline* and in *Quality*.
2. *File > Build Profiles > Meta Quest > Switch Platform* (Unity 6.1+).
3. *Project Settings > XR Plug-in Management*: Android tab **OpenXR**, with the Meta XR
   feature group (Meta XR Feature, Foveation, Subsampled Layout). Windows tab **OpenXR**
   for Link and the simulator. Disable any other loader.
4. *Player Settings*: set your own package name (for example `com.<you>.handover`) before
   uploading to the Meta dashboard.

## 4. Build the scenes

- **Handover > Create Meta XR Bootstrap Scene** builds `Assets/Handover/Content/Scenes/Bootstrap.unity`:
  the Interaction SDK comprehensive rig (Meta's Quick Actions), passthrough, MRUK, the
  Handover app wired to the rig's two tracked hands, and a hand menu with ray and poke
  interaction. It logs a report; read the warnings. A step that fails is reported and
  skipped, and the scene is still saved; fix the cause and run the menu again (answer
  **Don't Save** for the previous scene).
- Then **Window > Meta > Tools > Project Setup Tool**, **Fix All** on the Android tab *and*
  the Windows (desktop) tab.
- **Handover > Create Greybox Scene (keyboard hands)** builds a desktop scene with keyboard
  palms on a virtual tabletop (no headset, no simulator).

Commit the generated scenes and their `.meta` files once they work.

## 5. Run

### Greybox (desktop, keyboard palms)

Press Play in `HandoverGreybox.unity`. The HUD (O toggles it) shows the state. If the
water and dry areas look **magenta**, the URP asset from section 3 is not assigned yet
(older builds of the greybox used a URP shader regardless; the current code falls back
to a built-in shader).

| Keys | Action |
| --- | --- |
| Tab | switch the active hand |
| W A S D, Q E (Shift = fine) | move the active palm; down / up |
| Z / X, C | tilt the palm; level it |
| F | palm-down (shield pose) |
| H | hide / show the hand (tracking loss) |
| R | recenter (new origin epoch) |
| 1 2 3 4 | lift, then snap beside D / R / U / H |
| G | raise the active palm and bring the other alongside (handover pose) |
| V | raise the other hand as a shield over the carried doll |
| P or Space | pause / resume |
| Enter | confirm (stage, recovery, replay) |
| Backspace | restart checkpoint |
| K L M Y U | steady mode, relaxed mode, shelter assist, large cues, reduced motion |

A full chapter by keyboard: `1` (board Keeper), `G` (teaching hop), `Tab`, `3` (Keeper to
the lantern), `Tab`, `1` (Pip), `2` (rest at R), `2`, `4` (harbour), `1` (Luma), `4`,
`3` (Keeper), `4`.

### Meta XR Simulator

Install the standalone Meta XR Simulator and restart Unity, then **Window > Meta > Meta XR
Simulator > Activate** (greyed out until it is installed). Open `Bootstrap.unity`, press Play.
**Bar: zero errors and zero exceptions in the Console** (Interaction SDK asserts its
wiring in `Start()`). Hand poses in the simulator are discrete, so use the greybox and the
deterministic tests for fine thresholds. Simulator results are not hardware results.

### Tests

*Window > General > Test Runner > EditMode > Run All.* The same suite runs without Unity:
`./tools/run-core-tests.sh` (163 tests at the time of writing). The key sequence above is
replayed by `KeyboardWalkthroughTests` with the driver's placement maths.

## 6. Android build (Gate A)

*File > Build Profiles > Build*. A successful APK proves compilation and packaging only;
runtime behaviour is unverified until a device launches it.

## What still needs a person with the Editor

- Opening the project and committing the lockfile and generated settings.
- Running both scene builders, the Project Setup Tool, Play mode in the simulator.
- Confirming `MetaHandPoseSource` picked the rig's tracked hands (the builder logs which).
- Checking that MRUK asks for the scene permission with a clear explanation, and that a
  declined permission falls back to the virtual tabletop.
- Hardware gates D and E (see [`RemoteTestSheet.md`](RemoteTestSheet.md)).
