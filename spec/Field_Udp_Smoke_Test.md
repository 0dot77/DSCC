# Field UDP Smoke Test

Use this check after starting the DSCC native trackers or the Tauri tracker
supervisor. It verifies that the app-facing skeleton UDP stream contains all
four stations, decodes cleanly, and includes full K4ABT-style 32-joint skeleton
frames.

## One-Command Field Smoke

Use the wrapper script for normal field checks. It builds the required tools,
runs the selected gate, and exits non-zero on failure.

Quick readiness report:

```powershell
.\tools\Get-FieldReadinessReport.ps1
```

When cameras are connected, include the optional device check:

```powershell
.\tools\Get-FieldReadinessReport.ps1 -CheckDevices
```

Before a field handoff, require the Tauri process and receiver socket too:

```powershell
.\tools\Get-FieldReadinessReport.ps1 -CheckDevices -RequireAppProcess -Strict
```

Full field acceptance after four cameras are pinned and the Tauri player is
open:

```powershell
.\tools\Run-FieldAcceptance.ps1 -ConfigureTee -NoBuild
```

`Run-FieldAcceptance.ps1` runs the strict readiness report, the camera-free
Tauri app smoke through a probe tee, and the live app-fed performer motion
drill. `-ConfigureTee` explicitly changes DSCC `unity.skeletonPort` to the tee
input port, normally `55130`, with the existing backup behavior from
`Set-FieldSkeletonPort.ps1`. Use `-PlanOnly` first to print the exact sequence
without changing config or running the gates; plan mode still runs a read-only
strict readiness snapshot so current device, serial, app receiver, and avatar
blockers are visible before field handoff.

By default, `Run-FieldAcceptance.ps1` writes a transcript under
`artifacts\field-acceptance\field-acceptance-YYYYMMDD-HHMMSS.log`. Use
`-LogPath <path>` to choose the evidence file explicitly, or `-NoTranscript`
when running a quick dry check where no log should be created.
After a successful `-ConfigureTee` acceptance run, the wrapper restores direct
DSCC-to-Tauri routing so the Tauri player can keep receiving live skeletons
after the probe exits. Use `-KeepTeeAfterSuccess` only when another process will
continue forwarding the tee input port to the Tauri receiver.

If a step fails after `-ConfigureTee` changed the DSCC skeleton port, the
wrapper restores direct DSCC-to-Tauri routing by default, exits non-zero with
the failed step name, then closes the transcript. `-RestoreDirectOnFailure` is
kept as a compatible explicit form of that default behavior. Use
`-KeepTeeAfterFailure` only for diagnostics where the tee input port should
remain configured after a failed acceptance step.

Use `-Strict` in CI or before a field handoff when missing station serials,
invalid ports, or a missing Tauri receiver should return a non-zero exit code.
The report prints the current DSCC/Tauri UDP route, station serial pin status,
the four-Mega sync role contract (`station 1 = Primary`, `stations 2-4 =
Secondary`), CUDA body tracking config, the core head/hand/knee/ankle/foot
motion drill, Tauri station/model compatibility, the `StageScene.tsx` retargeter
source contract, GLB core retarget bone matches for pelvis, head, separated
wrist/hand target bones, knees, ankles, and feet, the one-Active-body and
selected-body stability gate, the Tauri process receiver socket, blocking
operator actions, and the next command to run. `-CheckDevices`
also runs the same K4A-wrapper device discovery path used by live tracking.
Before serial pinning it prints a physical-order pin template; after serial
pinning it verifies that the connected four `FemtoMega` serials match the field
config. `-RequireAppProcess` makes a missing Tauri player process blocking
instead of advisory; `-Strict` also treats the missing player as blocking and
exits non-zero when any issue is found.

Config-only preflight:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode Config -ConfigPath config\wall-a.local.json
```

This checks the field config, local K4A body tracking runtime, CUDA/cuDNN DLLs
required by the forced `Cuda` tracking mode, and Tauri app config compatibility
(UDP port, station ids, avatar references, and local model files). Config mode
reports all readiness failures before returning non-zero, so one run gives the
full readiness list.

Camera-free loopback:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode Offline
```

Tauri app receiver smoke without cameras:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode App -DurationSeconds 15 -NoBuild
```

Tauri app receiver smoke through the same UDP probe tee path used by live
validation:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode App -ForwardToApp -DurationSeconds 15 -NoBuild
```

Start the Tauri player first, press `d` to show its debug overlay, then run the
App mode. It sends four fake DSCC skeleton streams to the Tauri receiver port
from `config\show.local.json`. The wrapper first compares DSCC
`unity.skeletonPort` with the Tauri `receiver.port` and fails if they do not
match. It also checks that the Tauri `StageScene.tsx` retargeter still contains
the `Head -> Nose`, `WristLeft -> HandLeft`, `WristRight -> HandRight`,
`AnkleLeft -> FootLeft`, and `AnkleRight -> FootRight` paths, the `Head -> Nose`
confidence gate, and the corresponding Nose/hand target bone aliases. Before
sending to the app, it loopback-validates the fake stream for four stations, 32
joints, one selected body, confidence, and pelvis-relative head, hand, knee,
ankle, and foot joint motion. `App` mode requires a running Tauri player process by default so
the check cannot pass while only the sender/probe loopback is working. Use
`-AllowMissingAppProcess` only when you intentionally want to send a diagnostic
UDP stream without asserting that the app is open. When the app process is
required, the smoke also requires that process to own the Tauri receiver UDP
socket, normally port `55010`. The wrapper waits up to
`-ReceiverSocketTimeoutSeconds` seconds, default `5`, for the receiver socket to
appear after the app is detected.

Live field gate after DSCC tracking is already running:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode Live -ConfigPath config\wall-a.local.json -DurationSeconds 60
```

Use `-NoBuild` when the tools were already built and only the gate should be
rerun. Offline mode uses UDP port `55128` by default. Live mode reads
`unity.skeletonPort` from the config unless `-Port` is supplied. Live mode also
checks that four connected K4A-wrapper devices are visible as `FemtoMega` and
that their serials match the enabled station serials in the config.

Final performer motion drill after the stable live gate passes:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode Live -RequireMotionDrill -DurationSeconds 60 -NoBuild
```

During this run, ask the performer in each station to move head, both hands,
knees, ankles, and feet. The wrapper adds `--min-active-joint-motion-m 0.05` for
`Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight`, measured
relative to pelvis, so the gate fails if skeleton packets are present but the
tracked limbs/head are not actually moving. Live, offline, and app smoke gates
also require those same app-driving joints to be confident in at least 80% of
Active samples, so a mostly complete skeleton cannot pass while the head,
hands, knees, ankles, or feet are individually unusable.

Live field gate while also feeding the Tauri player:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode Live -ForwardToApp -Port 55130 -DurationSeconds 60 -NoBuild
```

For this tee mode, set DSCC `config\wall-a.local.json`
`unity.skeletonPort` to the probe input port, for example `55130`, while the
Tauri app `config\show.local.json` `receiver.port` stays on its normal app
port, normally `55010`. The wrapper verifies that DSCC sends to the probe input
port, that the app listens on a different receiver port, that the Tauri process
owns that receiver socket, then the probe validates the live four-station stream
and forwards every successfully decoded DSCC packet to the Tauri receiver.

Use the helper instead of editing JSON by hand:

```powershell
.\tools\Set-FieldSkeletonPort.ps1
```

Without `-Mode`, the helper only prints the current routing status and does not
write config.

```powershell
.\tools\Set-FieldSkeletonPort.ps1 -Mode Tee -Port 55130
```

To go back to direct DSCC-to-Tauri sending:

```powershell
.\tools\Set-FieldSkeletonPort.ps1 -Mode Direct
```

Recommended rehearsal order:

1. `Get-FieldReadinessReport.ps1`: shows the current routing mode, serial
   pinning gaps, Tauri receiver status, and the next command.
2. `Get-FieldReadinessReport.ps1 -CheckDevices`: with cameras connected,
   verifies CUDA/body-tracking runtime files and either prints the serial
   pinning template or validates the four pinned Femto Mega serials.
3. `-Mode Offline`: verifies DSCC wire format and probe gates without cameras or
   the Tauri app.
4. `Run-FieldReplaySmoke.ps1 -NoBuild`: verifies capture/replay evidence can be
   recorded, paced back into UDP, and accepted by the field-strict probe.
5. `-Mode App`: verifies the Tauri player can receive DSCC-format four-station
   fake skeleton frames, that the DSCC/Tauri UDP ports match, and that
   `StageScene.tsx` still contains the head/Nose, hand, and foot retarget paths.
6. `-Mode Config`: verifies field config, station pinning, K4A body tracking
   files, CUDA, cuDNN, the four-Mega sync roles, Tauri app config
   compatibility, the StageScene retargeter source contract, and avatar
   wrist/hand target bone compatibility. This is expected to fail until station
   serials are pinned.
7. `-Mode Live`: verifies the real four-Femto-Mega stream after serial pinning
   and live tracking startup.
8. `-Mode Live -RequireMotionDrill`: final direct-mode proof that live
   skeletons contain moving `Head`, `Nose`, both hands, both knees, both ankles, and both feet.
9. `-Mode Live -ForwardToApp`: verifies the same real stream and feeds the
   Tauri player at the same time, using a separate tee input port.
10. `-Mode Live -ForwardToApp -RequireMotionDrill`: final proof that the app-fed
   live stream contains moving head, hand, knee, ankle, and foot skeleton joints.
11. `Run-FieldAcceptance.ps1 -ConfigureTee -NoBuild`: one-command field handoff
    gate once the individual checks are understood.

By default the wrapper also requires each Active station stream to report no
more than one visible body (`--max-active-body-count 1`). Use
`-AllowExtraVisibleBodies` only when the physical camera overlap is known and
the selected body id stability gate is the intended acceptance criterion.
The probe prints `bodyStability=...` in station and acceptance summaries; use
`extraBodies>1`, `missingSelected`, `selectedIds`, and `bodyCounts` to tell
whether overlap was a brief spike, a persistent extra body, or a selected-body
tracking problem.

## Device Serial Check

Use this when wiring or replacing cameras. It lists the devices through the
same K4A wrapper path used by body tracking.

The shortest field path is the wrapper script. With all four cameras connected,
first print the physical-order template:

```powershell
.\tools\Set-FieldStationSerials.ps1 -PrintTemplate -NoBuild
```

After labeling the cameras from left to right on the wall, write the pins:

```powershell
.\tools\Set-FieldStationSerials.ps1 -Station1Serial LEFT_SERIAL -Station2Serial MID_LEFT_SERIAL -Station3Serial MID_RIGHT_SERIAL -Station4Serial RIGHT_SERIAL -NoBuild
```

The wrapper uses `DsccDeviceList` for the actual write, creates the same config
backup, runs field config validation, and then verifies the connected devices
match the pinned config. For rehearsal without cameras, use `-DryRun
-AllowUnconnected`; this writes only a temporary copy of the config.

For a repeatable camera-free rehearsal that leaves the generated config under
the repo `artifacts` folder:

```powershell
.\tools\Set-FieldStationSerials.ps1 -DryRun -AllowUnconnected -DryRunDirectory artifacts\field-serial-pin-dry-run -Station1Serial SERIAL_LEFT -Station2Serial SERIAL_MID_LEFT -Station3Serial SERIAL_MID_RIGHT -Station4Serial SERIAL_RIGHT -NoBuild
```

The dry-run command must report that the real config was not modified and that
the temporary pinned config passed field config validation. The serial values
still need to be replaced with the physical left-to-right Femto Mega serials
before running a real write. `-DryRunDirectory` is intentionally valid only with
`-DryRun`; a real write must not silently redirect or stage output elsewhere.

```powershell
dotnet run --project tools\DsccDeviceList -- --field
```

After `config\wall-a.local.json` has station serials filled in, verify the
connected devices match the config:

```powershell
dotnet run --project tools\DsccDeviceList -- --field --config config\wall-a.local.json
```

The check fails if fewer or more than four devices are visible, if any device is
not `FemtoMega`, if placeholder devices are returned, or if a configured station
serial is not connected.

To write the station serial pins, first label the physical cameras by station
position, then pass the explicit mapping. The command backs up the config before
writing and also disables `autoAssignDevicesOnStart`.

Print connected serials and a physical-order pinning template without writing
config:

```powershell
dotnet run --project tools\DsccDeviceList -- --field --print-pin-command config\wall-a.local.json
```

This output intentionally keeps the copyable command as a template. Replace
`LEFT_SERIAL`, `MID_LEFT_SERIAL`, `MID_RIGHT_SERIAL`, and `RIGHT_SERIAL` with the
actual physical left-to-right station serials before running the write command.
Any serial-sorted candidate printed by the tool is only a reference and must not
be run unless it matches the physical station order.

```powershell
dotnet run --project tools\DsccDeviceList -- --field --pin-config config\wall-a.local.json --pin-serials "1=SERIAL_1,2=SERIAL_2,3=SERIAL_3,4=SERIAL_4"
```

Do not rely on discovery order to decide station placement. Discovery order can
change with USB topology and reboot timing; station ids must come from the
physical labels used on site.

## Field Config Preflight

Run this before starting live tracking. It validates the field config without
waiting for UDP packets.

```powershell
dotnet run --project tools\DsccUdpProbe -- --check-field-config config\wall-a.local.json
```

Expected result:

```text
[pass] field config validation passed
```

The check fails if `autoAssignDevicesOnStart` is enabled, enabled stations are
not exactly `1,2,3,4`, any station is not `FemtoMega`, serials are missing or
duplicated, body tracking is not CUDA-only, `useLiteModel` is off, or the field
profile is not `NFOV_UNBINNED <= 15fps`.

The wrapper's `Config` and `Live` modes also run:

```powershell
dotnet run --project tools\DsccDeviceList -- --runtime --require-cuda
```

That catches missing K4A body tracking files and missing CUDA 11.4/cuDNN 8.2
runtime DLLs before the app is opened. This runtime-only command does not
require cameras to be connected; the connected-device check is the separate
`--field --config` step. The wrapper runs field config and runtime preflights
together before stopping, even if the config check fails.

## Live Four-Station Gate

```powershell
dotnet run --project tools\DsccUdpProbe -- 55010 30 --field-strict --expect-stations all
```

Expected result:

```text
[pass] validation passed
```

The command exits with code `2` when the stream is incomplete. Treat any failure
as a field setup problem before opening the actual player full-screen.
In validation modes, `DsccUdpProbe` also prints `[acceptance station N]` lines
summarizing stream rate/gaps, player/Active ratio, skeleton confidence, one-body
stability, selected body switches, and pelvis-relative head/hand/knee/ankle/foot
motion. A live gate is not acceptable until every enabled station summary reads
`stream=ok`, `player=ok`, `skeleton=ok`, `oneBody=ok`, and `motion=ok`.

## Live Tee Gate Into Tauri

Use this when the Tauri player should receive the same real stream that the
field probe is validating. The probe listens on the DSCC app-facing port and
forwards successfully decoded packets to the Tauri receiver port.

Recommended wrapper command:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode Live -ForwardToApp -Port 55130 -DurationSeconds 60 -NoBuild
```

Final app-fed motion drill:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode Live -ForwardToApp -RequireMotionDrill -Port 55130 -DurationSeconds 60 -NoBuild
```

Direct probe command:

```powershell
dotnet run --project tools\DsccUdpProbe -- 55130 60 --field-strict --max-active-body-count 1 --expect-stations all --expect-serials-from-config config\wall-a.local.json --forward-to 127.0.0.1:55010
```

Required port layout:

- DSCC `config\wall-a.local.json` `unity.skeletonPort`: tee input port, for
  example `55130`.
- Tauri `config\show.local.json` `receiver.port`: app receiver port, normally
  `55010`.
- These two ports must differ. The probe refuses same-port forwarding loops.

Set the DSCC tee input port with:

```powershell
.\tools\Set-FieldSkeletonPort.ps1 -Mode Tee -Port 55130
```

Switch back to direct mode with:

```powershell
.\tools\Set-FieldSkeletonPort.ps1 -Mode Direct
```

Expected result:

- The wrapper prints `DSCC unity.skeletonPort=55130; probe.listen=55130; Tauri receiver.port=55010`.
- The Tauri app process is listed and owns the receiver UDP socket.
- The probe prints `[forward] forwarded N packets ... errors 0`.
- The probe acceptance summary is the same as the live gate: all four stations
  must be `stream=ok`, `player=ok`, `skeleton=ok`, `oneBody=ok`, and
  `motion=ok`.
- The Tauri debug overlay shows the same real station frames while the gate is
  running.

## Live Serial And Stability Gate

After the four Femto Mega serials are fixed in the field config, run a longer
gate that proves each station is still bound to the intended physical camera
and keeps producing player skeletons.

Preferred config-based command:

```powershell
dotnet run --project tools\DsccUdpProbe -- 55010 60 --field-strict --max-active-body-count 1 --expect-stations all --expect-serials-from-config config\wall-a.local.json
```

Manual fallback:

```powershell
dotnet run --project tools\DsccUdpProbe -- 55010 60 --field-strict --expect-stations all --expect-serials 1=SERIAL_1,2=SERIAL_2,3=SERIAL_3,4=SERIAL_4
```

Use the actual serials reported by the DSCC device list or sidecar logs. This
gate should be run with one person standing in each station ROI. The config-based
command fails immediately if any enabled station is missing `device.serial` or
`calibration.cameraSerial`.

## Offline Loopback Gate

Use this when cameras are not connected. It proves the DSCC wire format,
multi-station sender, MessagePack decoding, validation gate, and that the fake
frames contain pelvis-relative head, hand, knee, ankle, and foot joint motion suitable
for avatar smoke checks.

Preferred wrapper command:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode Offline
```

Terminal 1:

```powershell
dotnet run --project tools\DsccUdpProbe -- 55128 5 --field-strict --max-active-body-count 1 --expect-device-type FakeSender --expect-stations all --expect-serials 1=FAKE-SENDER-001,2=FAKE-SENDER-002,3=FAKE-SENDER-003,4=FAKE-SENDER-004 --min-player-ratio 0.5 --min-active-ratio 0.5 --min-active-confidence 0.8 --min-active-joint-confidence-ratio 0.9 --required-active-joints Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight --min-required-active-joint-confidence-ratio 0.8 --min-active-joint-motion-m 0.05 --motion-joints Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight --max-player-gap-ms 3000 --max-active-gap-ms 3000
```

Terminal 2:

```powershell
dotnet run --project tools\DsccFakeSender -- 127.0.0.1 55128 all 2
```

The probe should report stations `1,2,3,4`, `maxJoints=32`, `decode errors 0`,
and `[pass] validation passed`.

## Tauri App Receiver Gate

Use this when cameras are not connected but the Tauri player should be checked.
It does not prove the app rendered correctly by itself; it provides a repeatable
four-station DSCC stream and prints the app process/port target so the operator
can confirm the debug overlay and avatars. The script first validates the same
fake stream locally with `DsccUdpProbe` so a broken sender or static skeleton
does not masquerade as an app issue.

Preferred wrapper command:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode App -DurationSeconds 15 -NoBuild
```

Direct command:

```powershell
.\tools\Run-TauriAppSmoke.ps1 -DurationSeconds 15 -NoBuild -RequireAppProcess
```

Expected result:

- The wrapper prints matching DSCC/Tauri skeleton ports, normally `55010`.
- The wrapper prints that the Tauri `StageScene.tsx` retargeter contract
  matched head/Nose, hand, and foot segments.
- The fake stream loopback preflight passes with `maxJoints=32`,
  `protocol=1`, `bodyCount=1`, and pelvis-relative head/hand/knee/ankle/foot
  `jointMotion` above `0.05m`.
- The required app-driving joints `Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight`
  are individually confident in at least 80% of Active samples.
- The Tauri app process is listed.
- The Tauri app process owns a UDP receiver socket on the configured receiver
  port.
- The Tauri debug overlay shows stations `1,2,3,4` receiving `FakeSender`
  frames.
- All four avatars enter and move from fake skeleton frames.
- After the fake stream finishes, the Tauri app process and receiver UDP socket
  are still present.

If the port check fails, align DSCC `config\wall-a.local.json` `unity.skeletonPort`
with Tauri `config\show.local.json` `receiver.port`. If `receiver.autoStart` is
false, start the receiver in the Tauri app before running the smoke.
Use `.\tools\Run-FieldSmoke.ps1 -Mode App -AllowMissingAppProcess` only for a
diagnostic send where the Tauri process requirement is intentionally disabled.
Use `-AllowMissingReceiverSocket` only to diagnose an already open app whose
receiver socket is intentionally not required.
If the app has just opened and the receiver starts slowly, rerun with a larger
`-ReceiverSocketTimeoutSeconds` value before treating it as a receiver failure.

For tee-mode app smoke, use:

```powershell
.\tools\Run-FieldSmoke.ps1 -Mode App -ForwardToApp -DurationSeconds 15 -NoBuild
```

This starts a temporary local `DsccUdpProbe` input port, sends fake DSCC
skeleton frames to that probe, validates the same four-station stream, forwards
successfully decoded packets to the Tauri receiver port, and fails if the probe
does not report clean forwarding.

## Capture And Replay A Live UDP Stream

Use this when the app misbehaves in the field and you need a reproducible
sample without keeping the cameras running. The capture file stores decoded
`StationSkeletonFrame` JSONL, including body count and selected body id.

Camera-free wrapper smoke:

```powershell
.\tools\Run-FieldReplaySmoke.ps1 -NoBuild
```

This records a temporary four-station `FakeSender` capture, replays it with
paced UDP output, and validates the replay through the same field-strict probe
gate used by live checks. The wrapper also fails if the probe summary is missing
per-joint confidence or motion evidence for any app-driving `Head`, `Nose`,
hand, knee, ankle, or foot joint, or the one-body `bodyStability` evidence. Use
this after changing capture/replay code or before relying on replay evidence in
the field.
Fake capture smoke requires at least four seconds so the generated sequence has
enough Active head/Nose motion to pass the field gate. `Run-FieldReplaySmoke`
automatically sizes the probe duration from the capture frame count and
`-SendIntervalMs` unless `-ProbeSeconds` is supplied explicitly.

Record the app-facing UDP stream:

```powershell
dotnet run --project tools\DsccUdpCapture -- record 55010 30 artifacts\field-capture.jsonl
```

Replay it back to the Tauri app or to another probe:

```powershell
dotnet run --project tools\DsccUdpCapture -- replay artifacts\field-capture.jsonl 127.0.0.1 55010
```

To replay into a separate probe instead of the app:

```powershell
dotnet run --project tools\DsccUdpProbe -- 55130 30 --field-strict --expect-stations all --expect-device-type FemtoMega
dotnet run --project tools\DsccUdpCapture -- replay artifacts\field-capture.jsonl 127.0.0.1 55130 --send-interval-ms 8
```

Use `--send-interval-ms` for validation replays so UDP packets are paced instead
of burst-sent. For a four-station capture, `8` ms is roughly 30 fps per station
when frames are interleaved by station. Plain `--no-timing` is useful only for
quick diagnostic sends where packet drops or receiver buffer pressure are not
part of the check.

Validate an existing real field capture through the wrapper:

```powershell
.\tools\Run-FieldReplaySmoke.ps1 -CapturePath artifacts\field-capture.jsonl -ExpectedDeviceType FemtoMega -ConfigPath config\wall-a.local.json -NoBuild
```

For real captures this expects station serials from the DSCC config by default.
Pass `-ExpectedSerials "1=SERIAL_1,2=SERIAL_2,3=SERIAL_3,4=SERIAL_4"` for a
one-off capture whose serials differ from the current config, or
`-NoSerialCheck` only when the capture predates serial pinning.

## Notes

- `--field-strict` applies the default live field gate: extra station failure,
  duplicate serial failure, 32-joint minimum, 10 fps minimum, player/Active
  requirements, 80% player/Active ratio, 0.45 Active confidence, 80% confident
  Active joints, 80% confident app-driving joints
  (`Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight`), 80% Active
  frames inside the station ROI, packet/player/Active gap limits, zero
  selected-body id changes, `FemtoMega` device type, current DSCC protocol
  version, and zero decode errors. Station ids and expected serials are intentionally still
  explicit.
- `--expect-device-type FakeSender` is only for offline loopback. Omit it for
  live checks so `--field-strict` requires `FemtoMega`.
- `--check-field-config config\wall-a.local.json` fails before live tracking if
  the config is not ready for the four-Femto-Mega field rig.
- `autoAssignDevicesOnStart` must stay `false` in the field config. Each station
  must be pinned to its physical Femto Mega by `device.serial` or
  `calibration.cameraSerial` so USB discovery order cannot remap stations.
- `device.syncRole` must be `Primary` for station `1` and `Secondary` for
  stations `2`, `3`, and `4`.
- `--expect-stations all` means stations `1,2,3,4`.
- `--fail-extra-stations` catches an accidental extra tracker process sending
  another station id to the same app UDP port.
- `--fail-duplicate-serials` catches two station ids publishing the same camera
  serial, which usually means process/device binding is wrong.
- `--forward-to host:port` tees every successfully decoded DSCC packet to
  another UDP receiver. Use a different probe listen port and app receiver port;
  never forward back to the same port.
- `--expect-serials 1=SER,2=SER` fails if a station receives frames from the
  wrong physical camera serial.
- `--expect-serials-from-config config\wall-a.local.json` reads expected serials
  from enabled stations in the DSCC config. It uses `device.serial` first and
  falls back to `calibration.cameraSerial`.
- `--min-joints 32` matches the native K4ABT joint count.
- `--require-player` catches the case where camera capture exists but body
  tracking never produces a player.
- `--require-active` catches streams that stay in `Empty`, `Entering`, `Lost`,
  or `Exited`.
- `--min-player-ratio` and `--min-active-ratio` catch unstable streams that only
  briefly detect a person.
- `--min-active-confidence` catches Active frames that are too weak to drive the
  app reliably.
- `--joint-confidence-threshold` and `--min-active-joint-confidence-ratio` catch
  frames where too many individual joints are below the retargeting confidence
  threshold.
- `--required-active-joints` and
  `--min-required-active-joint-confidence-ratio` catch cases where the overall
  skeleton looks good but a critical app-driving joint such as `Head`, `Nose`,
  a hand, knee, ankle, or foot is consistently low confidence. `--field-strict` enables this
  gate by default for the standard app-driving joints at an 80% ratio.
- `--min-active-joint-motion-m` is used by offline loopback and by
  `Run-FieldSmoke.ps1 -RequireMotionDrill` to prove the skeleton is animated
  enough to exercise avatar limb/head response. It is not part of bare live
  `--field-strict` because a real performer may intentionally stand still during
  field readiness checks.
- `--min-active-roi-ratio` catches cases where a body is tracked but the selected
  skeleton is not consistently inside the station ROI.
- `--require-active-inside-roi` is the strict version of the ROI gate and should
  be used after ROI calibration is finalized.
- `--min-active-marker-ratio` and `--require-active-inside-marker` are optional
  foot-marker gates. Use them only when `requireFootMarkerWhileActive` is part of
  the field setup.
- `--max-selected-body-id-changes 0` catches tracker identity swaps while a
  station is Active. This is part of `--field-strict`.
- `--max-active-body-count 1` is used by the wrapper by default because the
  field target is one person per Femto Mega. It is not part of bare
  `--field-strict` because neighboring station overlap can make K4ABT report
  extra bodies even when DSCC correctly selects the ROI body.
- `--max-frame-gap-ms` catches packet stalls from a station.
- `--max-player-gap-ms` and `--max-active-gap-ms` catch body-tracking dropouts
  even when the overall ratio still looks acceptable.
- `--min-fps 10` is a conservative lower bound for the app-facing stream. Raise
  it after the four-camera CUDA setup is stable.

## Duplicate Serial Drill

Use this only to prove the strict probe catches duplicate serials:

Terminal 1:

```powershell
dotnet run --project tools\DsccUdpProbe -- 55129 3 --expect-stations 1,2 --fail-extra-stations --fail-duplicate-serials --min-joints 32 --max-decode-errors 0
```

Terminal 2:

```powershell
dotnet run --project tools\DsccFakeSender -- 127.0.0.1 55129 1,2 2 DUPLICATE-SERIAL
```

Expected result: `camera serial DUPLICATE-SERIAL was observed on multiple
stations: 1,2`, and exit code `2`.
