# Toolchain and package versions

Selected on 6 November 2026 for the 7–17 November build. Do not chase a newer
SDK release during the sprint (Specification, Section 10). Change a version only
to fix a demonstrated defect, and record why here.

## Editor

| Item | Version | Why |
| --- | --- | --- |
| Unity Editor | **6000.6.8f1** (revision `237b6593792b`), Supported stream | What the project actually runs on since 8 Nov 2026: the developer machine opened it with 6000.6.8f1 and it compiled and ran the greybox. Downgrading back to the originally pinned 6000.3.28f1 LTS is riskier than staying, so **everyone uses exactly 6000.6.8f1** for the rest of the sprint. Meta XR Core SDK 207 requires at least 6000.0.66f2; Meta recommends 6.1 or later. |
| Editor modules | Android Build Support + OpenJDK + Android SDK & NDK Tools | Needed for the Quest APK. Install via Unity Hub. |

`ProjectSettings/ProjectVersion.txt` pins the editor. Unity Hub offers to install
it when you add the project. (It was first committed as 6000.3.28f1; the developer's
first-open commit records 6000.6.8f1.)

## Packages (`Packages/manifest.json`)

Meta packages come from Meta's npm registry (`https://npm.developer.oculus.com`,
scope `com.meta.xr`), declared as a scoped registry in the manifest.

| Package | Version | Notes |
| --- | --- | --- |
| `com.meta.xr.sdk.core` | 207.0.1 | Latest (12 Oct 2026). Pulls `com.unity.xr.hands` 1.7.2. |
| `com.meta.xr.sdk.interaction.ovr` | 207.0.1 | Interaction SDK with the OVR backend; pulls `com.meta.xr.sdk.interaction` (Essentials) 207.0.1. Installing only Essentials is the classic misinstall (no OVR rig). |
| `com.meta.xr.mrutilitykit` | 207.0.1 | MRUK for table detection. Pulls `com.unity.ai.navigation`, `com.unity.nuget.newtonsoft-json`. |
| `com.unity.xr.openxr` | 1.18.0 | Meta's recommended XR provider path is the Unity OpenXR plugin (1.15.1 or later). 1.18.0 (Aug 2026) predates Meta 207.0.1; 1.19.0 is only 11 days old and was not chosen. |
| `com.unity.xr.management` | 4.6.1 | Latest tagged release. |
| `com.unity.inputsystem` | 1.20.1 | Used only by the Editor keyboard driver. |
| `com.unity.render-pipelines.universal` | bundled with the editor | Core package. Pinned 17.3.0 for 6000.3; on 6000.6 Unity uses its own bundled version — `packages-lock.json` records the exact one. |
| `com.unity.test-framework` | bundled with the editor | Core package (1.6.0 on 6000.3); see the lockfile. |
| `com.unity.ugui` | bundled with the editor | Core package (2.0.0 on 6000.3, includes TextMesh Pro); see the lockfile. |
| `com.unity.ide.rider` / `com.unity.ide.visualstudio` | 3.1.0 / 2.0.29 | Editor convenience only. |

`Packages/packages-lock.json` was written on the first open (8 Nov, Unity 6000.6.8f1)
and is committed. Resolved there: Meta XR Core / Interaction / Interaction OVR / MRUK
**207.0.1**, OpenXR **1.18.0**, Input System **1.20.1**, XR Plug-in Management **4.7.0**
(Unity raised it from 4.6.1), XR Hands **1.9.0**, AI Navigation 2.0.16, and the
editor-bundled URP / SRP Core **17.6.0**, Test Framework **1.8.0**, uGUI **2.6.0**.
Unity 6.6 also adjusted the module list (added `physicscore2d`, `tetgen`,
`timelinefoundation`; dropped `vr`). The compile check in `tools/unity-compile-check`
still builds against the 6000.3.28f1 assemblies, so treat it as a name and signature
check only.

### Meta XR Simulator

Meta now distributes the XR Simulator as a standalone install (Meta Quest
Developer Hub, the developer downloads page, or `metavr tools install xrsim`).
The Unity package `com.meta.xr.simulator` stopped at 81.0.1 (Nov 2025) and is
not in the manifest. If the simulator toggle does not appear with Core SDK 207,
follow Meta's current setup guidance before adding that package; record the
outcome here. Windows has full simulator support; on macOS hand tracking is
limited.

## API facts checked against installed package source

These were read from the 207.0.1 package sources, not from memory:

- `Oculus.Interaction.Input.IHand`: `GetJointPose(HandJointId, out Pose)` returns
  a world pose; `IsTrackedDataValid`, `IsConnected`, `CurrentDataVersion`,
  `WhenHandUpdated`. There is no `TrackingToWorldSpace` member in this version.
- ISDK defines `ISDK_OPENXR_HAND` unconditionally in its own assembly, so the
  public `HandJointId` is the OpenXR skeleton (`HandPalm = 0`, `HandWristRoot = 1`,
  `HandMiddle1`, ...).
- Joint axis constants (`Oculus.Interaction.Input.Constants`): for both hands
  `Palmar = Vector3.down`, `Distal = Vector3.forward`.
- `OVRManager` static events: `HMDMounted`, `HMDUnmounted`, `InputFocusAcquired`,
  `InputFocusLost`, `TrackingAcquired`, `TrackingLost`; recenter via
  `OVRManager.display.RecenteredPose`.
- MRUK: `MRUK.Instance`, `RegisterSceneLoadedCallback`, `GetCurrentRoom()`,
  `MRUKRoom.Anchors`, `MRUKAnchor.Label` (`SceneLabels.TABLE`),
  `MRUKAnchor.PlaneRect` (`Rect?`, in the anchor's local XY plane).
- Assembly names: `Oculus.VR`, `Oculus.Interaction`, `Oculus.Interaction.OVR`,
  `meta.xr.mrutilitykit`; Editor: `Oculus.Interaction.Editor`,
  `Oculus.Interaction.OVR.Editor`, `Oculus.VR.Editor`.
- Quick Actions: `OVRQuickActionsAPI.AddOVRInteractionRig(bool generateAsEditableCopy = true)`,
  `QuickActionsAPI.AddRayCanvasInteraction(GameObject, bool)` and
  `AddPokeCanvasInteraction(GameObject, bool)`.
- `OVRProjectConfig.CachedProjectConfig`, `CommitProjectConfig`, `handTrackingSupport`,
  `sceneSupport`, `insightPassthroughSupport`; `OVRManager.isInsightPassthroughEnabled`.
- MRUK `RegisterSceneLoadedCallback` invokes the callback immediately when the scene is
  already loaded.
- Input System 1.20.1: `Keyboard.current`, `Keyboard[Key]` returning `KeyControl`
  (a `ButtonControl` with `isPressed` and `wasPressedThisFrame`).

These are the members the code uses; their signatures are mirrored in
`tools/unity-compile-check/stubs/` for the compile check.

## Engine-free test toolchain (not shipped)

| Item | Version |
| --- | --- |
| .NET SDK | 8.0.427 (any 8.0 SDK works) |
| NUnit / NUnit3TestAdapter / Microsoft.NET.Test.Sdk | 3.14.0 / 4.6.0 / 17.11.1 |
| Core target | `netstandard2.1`, C# 9 — Unity 6's API compatibility level and language version |
