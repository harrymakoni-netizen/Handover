# Toolchain and package versions

Selected on 6 November 2026 for the 7–17 November build. Do not chase a newer
SDK release during the sprint (Specification, Section 10). Change a version only
to fix a demonstrated defect, and record why here.

## Editor

| Item | Version | Why |
| --- | --- | --- |
| Unity Editor | **6000.3.28f1** (revision `8f082d2fe476`), LTS stream | Newest Unity 6 LTS patch on 6 Nov 2026. Meta's Unity setup guidance recommends Unity 6.1 or later; Meta XR Core SDK 207 requires at least 6000.0.66f2. |
| Editor modules | Android Build Support + OpenJDK + Android SDK & NDK Tools | Needed for the Quest APK. Install via Unity Hub. |

`ProjectSettings/ProjectVersion.txt` pins the editor. Unity Hub offers to install
it when you add the project.

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
| `com.unity.render-pipelines.universal` | 17.3.0 | Core package bundled with 6000.3 (verified from the editor's built-in packages). |
| `com.unity.test-framework` | 1.6.0 | Core package bundled with 6000.3 (uses `com.unity.ext.nunit` 2.0.3). |
| `com.unity.ugui` | 2.0.0 | Core package bundled with 6000.3 (includes TextMesh Pro). |
| `com.unity.ide.rider` / `com.unity.ide.visualstudio` | 3.1.0 / 2.0.29 | Editor convenience only. |

`Packages/packages-lock.json` does **not** exist yet. Unity writes it on the
first open; commit it together with the regenerated `ProjectSettings/*.asset`
files (Section 10: "commit both package manifest and lockfile").

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
  `meta.xr.mrutilitykit`.

## Engine-free test toolchain (not shipped)

| Item | Version |
| --- | --- |
| .NET SDK | 8.0.427 (any 8.0 SDK works) |
| NUnit / NUnit3TestAdapter / Microsoft.NET.Test.Sdk | 3.14.0 / 4.6.0 / 17.11.1 |
| Core target | `netstandard2.1`, C# 9 — Unity 6's API compatibility level and language version |
