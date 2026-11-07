# Remote tester sheet (Gates D and E)

Send this sheet with a versioned build (Meta release channel or sideloaded APK). A report
is evidence **only for this build and the conditions actually tried** (Specification,
Sections 9 and 16). Please record screen capture and, if you can, device logs
(`adb logcat -s Unity` or Meta Quest Developer Hub).

## Build and conditions

| Field | Value |
| --- | --- |
| Build (commit hash / app version) | |
| Headset and OS version | |
| Date, tester | |
| Room lighting (bright / dim / mixed) | |
| Table (size, height, surface colour) or virtual tabletop | |
| Seated? Forearms resting on the table? | |
| Room data present? Permission granted or declined? | |

## Gate D — hand interaction on hardware

Count attempts honestly; "first-attempt success" means the transfer completed without
retrying the pose. Target: 90 % first-attempt success in controlled poses, and **no lost
doll ever** (a doll that disappears, duplicates, or cannot be recovered).

| # | Task | Attempts | First-attempt successes | Notes |
| --- | --- | --- | --- | --- |
| D1 | Board Keeper onto the **left** palm from Ready (palm-start) | | | |
| D2 | Board Keeper onto the **right** palm from Ready (after Replay) | | | |
| D3 | 20 hops left → right, palms side by side, 1–4 cm apart | | | |
| D4 | 20 hops right → left | | | |
| D5 | Hops with mild palm overlap | | | |
| D6 | Hops with forearms resting on the table, hands close to it | | | |
| D7 | Hide the carrying hand (behind the back) for 2 s, then return it to the marker | | | |
| D8 | Hide the carrying hand, return it elsewhere, choose *Return to safety* | | | |
| D9 | Pause from the hand menu while carrying, then Resume | | | |
| D10 | Remove the headset mid-chapter, put it back, Resume | | | |
| D11 | Recenter (hold the Meta button) mid-chapter | | | |
| D12 | Fresh placement: Reposition from the pause menu and confirm again | | | |
| D13 | Decline the room permission: the virtual tabletop must appear | | | |
| D14 | Tilt a carrying palm slowly: warning appears, the doll crouches, never slides | | | |
| D15 | Shelter: carry Pip across the open middle with the other hand palm-down above | | | |

Also note: frame drops or stutter, any Console-visible errors in the log, and whether
the dolls look attached to the palm or float behind it.

## Gate E — a new player, hands only

Do not explain the mechanics; only say "Rescue everyone on the table." Note times from
first boarding.

| Observation | Value |
| --- | --- |
| Time to first boarding after the stage is confirmed | |
| Time to the first unaided palm-to-palm hop | |
| Route chosen for the first rescue (direct or via the low refuge) | |
| Did they understand why the harbour was dark / the low refuge closed? | |
| Any reach or comfort problem, fatigue | |
| Total chapter time | |
| Failed actions and what the player blamed (themselves, the hands, the game) | |
| Would they replay? | |

If a player blames themselves for a tracking error, report it: the cue and recovery need
improving (Section 16).
