# DSCC K4ABT Native Tracker

Native sidecar for one Orbbec Femto Mega station.

The process owns exactly one K4A-compatible camera and one Azure Kinect Body Tracking tracker. It emits the existing DSCC `StationSkeletonFrame` MessagePack UDP packet, so the Tauri player can keep using the current `55010` receiver.

## Build

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "C:\Users\o77do\Developer\DSCC\tools\DsccK4abtTracker.Native\DsccK4abtTracker.Native.vcxproj" `
  /p:Configuration=Release /p:Platform=x64
```

Output:

```text
artifacts\dscc-k4abt-tracker\x64\Release\dscc-k4abt-tracker.exe
```

The build copies Orbbec K4A wrapper DLLs, `k4abt.dll`, ONNX Runtime, model files, and the CUDA/cuDNN runtime DLLs beside the executable.

## Run One Station

```powershell
.\artifacts\dscc-k4abt-tracker\x64\Release\dscc-k4abt-tracker.exe `
  --station-id 1 `
  --device-index 0 `
  --host 127.0.0.1 `
  --port 55010 `
  --processing-mode cuda `
  --depth-mode NFOV_UNBINNED `
  --fps 15
```

For four Femto Mega devices, run four processes with distinct `--station-id` and either fixed `--serial` or fixed `--device-index`. Serial binding is preferred for the field.

## Diagnostics

List K4A wrapper devices and serials:

```powershell
.\artifacts\dscc-k4abt-tracker\x64\Release\dscc-k4abt-tracker.exe --list-devices
```

Send one protocol-compatible mock frame to the Tauri/DSCC receiver:

```powershell
.\artifacts\dscc-k4abt-tracker\x64\Release\dscc-k4abt-tracker.exe `
  --mock-once `
  --station-id 1 `
  --host 127.0.0.1 `
  --port 55010
```

## Frame Contract

- Source coordinates: K4ABT depth-camera/global sensor space.
- Wire coordinates: meters, matching `JointFrameDto.PositionLocal` in the existing DSCC protocol.
- Rotations: K4ABT absolute sensor-space joint quaternions.
- Confidence: K4ABT joint confidence mapped to `0.0`, `0.33`, `0.66`, `1.0`.
- Multiple people: keep the previous body id when present; otherwise choose the highest-confidence body.
