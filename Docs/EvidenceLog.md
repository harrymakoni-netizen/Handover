# Evidence log

Every entry names the commit, the environment and the kind of evidence.
Deterministic evidence proves rule consistency only; it does not establish real
tracking, comfort or frame timing (Specification, Section 16). Never record an
Editor or simulator run as a headset result.

## Gate status

| Gate | Deterministic (.NET / EditMode) | Editor / XR Simulator | Hardware (Quest 3) |
| --- | --- | --- | --- |
| A Setup | Keeper boarding starts play exactly once (tests); Unity-side code compiles against Unity 6000.3.28f1 assemblies; **10 Nov:** EditMode suite inside Unity 6000.6.8f1, 163/163 | **Partial (8 Nov):** opened in Unity 6000.6.8f1 on Windows, compiled, greybox scene built and ran in Play mode; URP asset assigned. **8–9 Nov:** Bootstrap scene built; Play mode with 0 errors and 0 warnings; Project Setup Tool required and recommended fixes applied; XR Simulator 2.0 runtime connected, hands shown. **10 Nov:** Android development APK built from the command line, 0 errors (compiles and packages only). Still to do: hand menu and stage confirmation in the simulator, boarding (needs webcam hands or a headset) | Not run |
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

### Unity integration layer — compile check only

- What exists: `Handover.Runtime` (HandoverApp composition root, keyboard and
  transform hand sources, greybox stage, peg dolls, palm feedback, procedural
  audio through the cue arbiter, hand menu, debug HUD), `Handover.Platform.Meta`
  (Interaction SDK hand adapter, OVRManager lifecycle bridge, MRUK table source)
  and two Editor scene builders (Meta XR Bootstrap scene, keyboard greybox scene).
- Evidence: `tools/unity-compile-check/run.sh` built every Unity-side assembly with
  the .NET SDK against the **Unity 6000.3.28f1 Linux editor's** module DLLs, with
  `UnityEngine.UI` compiled from that editor's bundled uGUI source, in seven
  variants (Android player with the new Input System, with legacy input, Editor;
  Meta adapter player and Editor; both Editor tool assemblies), warnings as errors.
  All passed. Input System and Meta SDK members come from signature stubs copied
  from com.unity.inputsystem 1.20.1 and Meta XR SDK 207.0.1 sources.
- Not verified (needs a person with the Editor): asmdef resolution, serialization,
  Play mode, Interaction SDK wiring asserts, the scene builders, the Project Setup
  Tool, the XR Simulator, an Android build, and anything on a headset.

### Re-arm rule narrowed to the berth just used

- Found by tracing the keyboard walkthrough in Setup.md: the re-arm rule was keyed
  by (hand, dock), so a hand that boarded Keeper from Departure could not board
  Pip from the neighbouring Departure berth until it moved 7 cm away. Now keyed by
  (hand, berth): only the berth just used needs separation.
- Correction: commit `0f63ca8`'s message says "162 passed at this commit"; re-run in a
  clean worktree of that commit, the suite has **159** tests, all passing (the caption and
  walkthrough tests arrived in `47a8906`).
- Evidence: 163 passed at `47a8906`. New `TheSameHandBoardsAnotherBerthOfTheDockWithoutMovingAway`
  fails with the old per-dock rule (mutation check) and passes now;
  `KeyboardWalkthroughTests` replays the documented key sequence with the driver's
  placement maths and completes the chapter.

### First Editor run (developer machine, 8 Nov 2026)

- Environment: Windows, Unity **6000.6.8f1** (DX11), Meta XR SDK 207 welcome window shown.
  Evidence: the developer's screenshots; I did not see the full Console.
- Observed: the project opened without Safe Mode; the **Handover** menu exists (Editor
  assemblies compiled); *Create Greybox Scene* built `HandoverGreybox.unity`; Play mode
  ran with the keyboard driver, the HUD reported live state (both synthetic palms
  tracked and carry-eligible; Keeper, Pip and Luma at Departure; session paused from
  the keyboard) and the hand menu rendered its Pause panel.
- Problem found: the greybox's transparent pieces rendered **magenta** because no URP
  asset was assigned and the material used a URP shader. Fixed: the material now picks
  a built-in shader when no render pipeline asset is active (compile-checked); the URP
  setup step is still required.
- Meta's Project Setup Tool reported 4 outstanding items (expected before Fix All).
- Not yet observed: EditMode tests in Unity, a full keyboard chapter, the Bootstrap
  scene, the XR Simulator, an APK.

### First Bootstrap scene build (developer machine, 8 Nov 2026)

- Environment: Windows, Unity 6000.6.8f1, Android platform. Evidence: one screenshot.
- Observed: *Create Meta XR Bootstrap Scene* added the comprehensive rig, MRUK and the
  Handover root, then stopped with `AssertionException: At GameObject HandSphereMap,
  component FromHandPrefabDataSource. Required Hand Skeleton Provider reference is missing`.
  The menu was never built and the scene stayed *Untitled* (never saved).
- Cause (my bug, confirmed in the 207.0.1 sources): the builder read `Hand.Handedness` on
  every exact-type `Hand`. That property evaluates `GetData()`, and the rig's synthetic
  HandSphereMap hand is fed by a `FromHandPrefabDataSource` whose skeleton provider is only
  assigned in `Awake`, so in Edit mode it throws. The same loop also ran at runtime in
  `Awake`, and could have picked that synthetic hand (it always reports itself as tracked).
- Fix: `MetaRigHands` selects only exact-type `Hand`s whose modifier chain ends at
  `FromOVRHandDataSource`, following serialized references without evaluating data. The
  builder reads the side from that source's serialized `_handedness`; the runtime fallback
  searches on the first `Poll` (after every `Start`). Sampling uses the wired side, not
  `Hand.Handedness`. Each builder step now reports its own failure and the scene is always
  saved. Evidence: the compile check passes (7/7) with the data-source types added to the stubs;
  the core suite still passes (163). **Not yet re-run in the Editor.**

### Bootstrap scene, Project Setup Tool and XR Simulator (developer machine, 8–9 Nov 2026)

- Environment: Windows, Unity 6000.6.8f1, Android platform, Meta XR Simulator 2.0 (standalone).
  Evidence: the developer's screenshots; I have not seen every Console line.
- The fixed builder (`aa8e080`) completed: its report was logged as a normal message (it logs a
  warning when any step fails), 0 errors. The scene has the comprehensive rig, MRUK, Handover,
  Handover Menu and ISDK's PointableCanvasModule.
- Play mode without an XR runtime: **0 errors, 0 warnings** (Interaction SDK start-up asserts
  passed; no "could not find both tracked hands" warning, so the hands are referenced); the HUD
  showed the stage prompt.
- Project Setup Tool: required and recommended fixes applied on both tabs; 6 manual-only items
  remain. *Fix All* on the Windows tab also activated Meta XR Operator (not needed; harmless so far).
- XR Simulator: "XR Simulator runtime enabled" with Unity in Play mode; inputs switched from
  controllers to hands and both simulated hands rendered. Stage confirmation, the hand menu and
  boarding are not yet observed.
- Problem found (my bug): in the Editor, the Unity window losing OS focus (which happens whenever
  you click into the simulator) raised `OnApplicationFocus(false)` and `OnApplicationPause(true)`;
  both paused Handover, and Resume is refused without focus, so the game could not be played
  from the simulator window. Also `runInBackground` was off, so Play mode stops updating while
  another window has focus.
- Fix: in the Editor, OS window focus is ignored unless *Editor Window Focus Pauses* is ticked
  (renamed field, default off, so existing scenes drop the old `true`); the headset's focus and
  mount signals still pause, and device behaviour is unchanged. The Bootstrap builder turns on
  Run In Background. Compile check 7/7. Not yet re-run in the Editor.
- Second simulator Play in the same Editor session (after choosing a synthetic environment):
  999+ errors, `Unable to set TrackingOrigin Floor to Unity Input Subsystem` every frame and
  `[OVRPlugin] [xrCreateReferenceSpace] session should not be XR_NULL_HANDLE`: no OpenXR session
  was created. This matches Meta forum reports (simulator works once per Editor session; synthetic
  environments with a floor-level origin). After restarting Unity, without an environment:
  **0 errors**.
- That run showed 999+ warnings: `MRUK EnableWorldLock is enabled and is controlling the tracking
  space position. Tracking position was set to (0.00, -0.12…-0.20, 0.00)`. Cause, from the 207.0.1
  sources: the comprehensive rig's ISDK `FirstPersonLocomotor` applies gravity and moves the
  TrackingSpace every frame (no floor collider), and MRUK's world lock moves it back, so head and
  hands jitter. Handover is seated: the Bootstrap builder now disables `FirstPersonLocomotor`
  (component off; Awake still runs). Compile check 7/7. Existing scenes: untick it by hand.
- With `FirstPersonLocomotor` unticked by hand in the scene (9 Nov, about 21:26): 0 errors, 23
  warnings instead of 999+, and the HUD reported the right hand **ok**, so simulated hand data
  reaches `MetaHandPoseSource` (the left hand's state was cut off in the screenshot). The warnings
  seen were the simulator's own: fallback chessboard passthrough (no synthetic environment) and
  MRUK finding no scene data in the simulator. Not confirmed: whether the untick was saved
  outside Play mode.
- **Unresolved, developer machine:** after its window was closed, the standalone simulator app
  (`MetaXRSimulator.exe`) no longer shows a window, on Play or when launched from Windows
  (`xrsim://`, as the developer reports). Its runtime still serves Play mode. The SDK's Editor
  code only sets `XR_RUNTIME_JSON` for the Unity process, so the project cannot be what stops the
  app. A restart and a reinstall of the simulator are untried. Without the window there is no
  head or hand control, so the hand menu, stage confirmation and boarding remain **not observed**
  in the simulator.

### First Android test build (developer machine, 10 Nov 2026)

- Environment: Windows, Unity 6000.6.8f1, batch mode, commit `092902b` plus the developer's
  uncommitted changes to the Bootstrap scene and project settings. Evidence: the build log
  (`Logs/handover-android-build.log`, not committed) and the APK on disk.
- Command: `Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod
  Handover.EditorTools.HandoverBuild.BuildAndroidTestApkBatch -logFile ...`.
- First attempt did not start: Unity exited with code 198, `No valid Unity Editor license found`
  (Unity Hub was not running or signed in). Not a project fault. After the developer signed in
  to the Hub and the Editor was closed, the second attempt ran.
- Result: `[Handover] Android test build SUCCEEDED`, exit code 0, **0 errors, 7 warnings**
  (warnings not inspected), 36.4 min for a first build. Development build, debug-signed, scene
  Bootstrap only, package `com.DefaultCompany.Handover`, version 1.0 (1).
- Output: `Builds/Handover-test.apk`, 91,199,946 bytes (87 MB). The build summary printed
  1213.3 MB: `BuildSummary.totalSize` is not the size of the APK file, so that line of the
  report is misleading and should be corrected.
- The working tree showed further changes afterwards that were not made by hand in this
  session (`Assets/HandoverURP.asset`, `Assets/Oculus/OculusProjectConfig.asset`,
  `Assets/UniversalRenderPipelineGlobalSettings.asset`, new `Assets/Resources/OVRBuildConfig.asset`,
  `.utmp/`). Whether the build or the earlier Editor session wrote them is not established.
- What this shows: the project compiles (IL2CPP) and packages for Android. It is **not** a
  headset test: the APK has not been installed or run on a Quest 3. Hardware column unchanged.

### EditMode tests inside Unity and build follow-ups (developer machine, 10 Nov 2026)

- Environment: Windows, Unity 6000.6.8f1, batch mode, commit `19b4354` plus the `HandoverBuild`
  and `.gitignore` changes below. Deterministic evidence only.
- EditMode tests in Unity (`-batchmode -runTests -testPlatform EditMode`): exit code 0,
  **163 passed, 0 failed, 0 skipped** (`Logs/editmode-results.xml`, not committed). No compiler
  errors in the log, so the Unity-side assemblies, including the changed `HandoverBuild`, compile
  in the real Editor.
- `tools/run-core-tests.sh`: **163 passed, 0 failed**. This machine has only the .NET 10 runtime,
  so the net8.0 test project needed `DOTNET_ROLL_FORWARD=Major`; without it the run aborts before
  any test.
- `tools/unity-compile-check/run.sh` against the 6000.6.8f1 editor: **did not work, not a
  result**. It is written for 6000.3 (the bundled uGUI source in 6000.6 uses types the check
  does not reference) and its stub paths do not resolve under Git Bash on Windows
  (`C:\c/Users/...`). The Editor compile above covers the same code on this machine.
- `HandoverBuild` now reports the APK size from the file on disk instead of
  `BuildSummary.totalSize`. Compiled, but not exercised: no new Android build was run.
- The 7 build warnings, read from the build log: URP Terrain shaders with all subshaders removed
  on Android (not used by Handover), and CS0618 at `MetaRigHands.cs(45)`:
  `FindObjectsByType<T>(FindObjectsSortMode)` is obsolete in 6000.6. Left as is, because the
  replacement overload has not been checked against 6000.3.
- `.utmp/` (temporary files that appeared beside the build; origin not established) is now
  ignored by git.

### Open items on the developer machine (as of 10 Nov 2026)

1. Bootstrap scene: confirm `PlayerController` > *First Person Locomotor* is unticked and saved.
2. Player Settings (PC tab) > *Run In Background* on (the committed settings still have it off).
3. *Window > Meta > Meta XR Operator > Deactivate* (not needed; activated by Fix All).
4. Delete `Assets/f.unity` if it was not saved on purpose (it looks like the first, broken scene).
5. Simulator window: see above. Optional; do not let it block the build.
6. Android build and EditMode tests inside Unity: done on 10 Nov (see above). Next, independent
   of the simulator: a headset run with `Docs/RemoteTestSheet.md` (needs a Quest 3).
7. `MetaRigHands.cs(45)`: replace the obsolete `FindObjectsByType` overload once the new one is
   confirmed in the installed Unity source.
8. `tools/unity-compile-check` does not run against 6000.6 or under Git Bash on Windows.
