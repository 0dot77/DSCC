# Orbbec/Unity Retargeting Research Notes

Last reviewed: 2026-05-26

This note is the reference to read before changing DSCC skeleton transport, Unity coordinate mapping, avatar scale, humanoid IK, or direct-bone retargeting code.

## Problem Observed

In the current saedaegal test scene, the live DSCC skeleton is received correctly, but the neck appears stretched in the debugger and the avatar head can over-rotate. The root cause is not a Unity bone scale mutation. The current profile uses `AvatarScale: 5`, and both runtime target placement and debug drawing use this same global scale:

```text
world = anchor + (joint - pelvis) * AvatarScale
```

That makes the whole tracked human skeleton five times larger, including `Neck -> Head`. This was introduced to roughly match the stylized wing span, but it is the wrong abstraction for head/neck and debug visualization.

## Source Findings

### Orbbec Femto Bolt and Azure Kinect Compatibility

Orbbec states that Femto Bolt uses Microsoft iToF depth technology and has Azure Kinect-compatible depth modes/performance. Orbbec also states that its Azure Kinect Sensor SDK wrapper lets applications built for Azure Kinect SDK, including Microsoft Body Tracking SDK applications, run with Femto Bolt/Mega devices.

Relevant implications for DSCC:

- Treat Femto Bolt body tracking data as Azure Kinect Body Tracking style data when using the K4A wrapper.
- Depth mode/FPS limitations matter. For example, WFOV unbinned is 1024x1024 at 5/15 fps, while NFOV unbinned is 640x576 at up to 30 fps.
- Do not assume color FPS equals body tracking FPS. Body tracking is depth + inference bound.

Source:

- Orbbec Femto Bolt product/FAQ and comparison: https://www.orbbec.com/products/tof-camera/femto-bolt/

### Azure Kinect Body Tracking Joint Data

Microsoft documents Azure Kinect body tracking as a kinematic skeleton with 32 joints. A joint's position and orientation are estimates relative to the global depth sensor frame. Position is in millimeters in the native API; orientation is a normalized quaternion. Each joint coordinate system is absolute in the depth camera 3D coordinate system, not a Unity avatar-local bone transform.

The documented hierarchy places:

- `NECK` parent: `SPINE_CHEST`
- `HEAD` parent: `NECK`
- `NOSE`, `EYE_LEFT`, `EAR_LEFT`, `EYE_RIGHT`, `EAR_RIGHT` parent: `HEAD`

The K4ABT C API docs also state that `k4abt_joint_t` contains position, orientation, and confidence, and that those define the joint coordinate system relative to the sensor global coordinate system.

Confidence semantics are important:

- `NONE`: joint out of range.
- `LOW`: joint is predicted, likely occluded.
- `MEDIUM`: medium confidence; current SDK commonly provides up to this level.
- `HIGH`: listed but effectively a placeholder in the referenced docs.

Relevant implications for DSCC:

- `JointFrameDto.PositionLocal` is sensor/depth-camera-space data converted to meters by DSCC, despite the local name.
- `JointFrameDto.RotationLocal` is an absolute sensor-space joint orientation, not a local rotation relative to the avatar bone's parent.
- Directly applying K4ABT orientation to stylized FBX bones without bind-pose calibration will twist or over-rotate.
- Face joints (`Nose`, eyes, ears) must be confidence gated. A low-confidence `Nose` should not drive head direction.

Sources:

- Azure Kinect body tracking joints: https://learn.microsoft.com/en-us/previous-versions/azure/kinect-dk/body-joints
- `k4abt_joint_t` structure: https://microsoft.github.io/Azure-Kinect-Body-Tracking/release/0.9.x/structk4abt__joint__t.html
- `k4abt_joint_confidence_level_t`: https://microsoft.github.io/Azure-Kinect-Body-Tracking/release/0.9.x/group__btstructures_ga8582ddc16801cb72d4d3691bcfc3cddb.html

### Sensor Coordinate Mapping

Azure Kinect depth camera data uses a camera/depth frame basis. A peer-reviewed Azure Kinect study describes the depth camera axes as x to the camera-right, y downward, and z forward from the camera perspective. The current Unity mapper remaps axes and optionally mirrors X.

Relevant implications for DSCC:

- Keep coordinate remapping centralized in `DsccCoordinateMapper`.
- Validate axis mapping from live joints before editing retargeting logic:
  - raising user's left hand must move DSCC `ShoulderLeft/ElbowLeft/WristLeft` on the expected Unity side;
  - `Pelvis -> Neck -> Head` should move upward in Unity after mapping;
  - `Head -> Nose` should point generally toward the user's face/camera direction, but only when face joint confidence is acceptable.
- Do not hide axis issues by adding arbitrary per-joint offsets. Offsets should be final avatar-fitting adjustments, not coordinate fixes.

Source:

- Azure Kinect processing mode study, coordinate-system discussion: https://pmc.ncbi.nlm.nih.gov/articles/PMC9860777/

### Unity Humanoid Retargeting and LookAt

Unity's Humanoid Avatar system maps animations between humanoid skeletons, but convincing results depend on proper avatar mapping and muscle limits. Unity documents per-muscle constraints such as head nod/tilt limits, and warns that translation DoF adds retargeting cost and should be enabled only when needed.

Unity's `Animator.SetLookAtWeight` separates global, body, head, eyes, and clamp weights. `clampWeight = 1` makes LookAt impossible; `clampWeight = 0` is unconstrained.

Relevant implications for DSCC:

- Use Humanoid IK for hands/feet only when the avatar proportions are close enough.
- For stylized characters, prefer constrained direct-bone or Animation Rigging targets over full Humanoid retargeting.
- `SetLookAtPosition` requires a point in front of the head, not the head joint's own position.
- Do not use global `AvatarScale` to solve stylized limb span and then reuse that same scale for head/neck. Segment scale must be split.

Sources:

- Unity `Animator.SetLookAtWeight`: https://docs.unity.cn/ScriptReference/Animator.SetLookAtWeight.html
- Unity Avatar Muscle & Settings tab: https://docs.unity.cn/2020.3/Documentation/Manual/MuscleDefinitions.html

## DSCC Design Rules From The Research

### 1. Separate Sensor Skeleton Scale From Avatar Segment Scale

Do not use a single `AvatarScale` for every target and every debug line. Split the scale concept:

- `SkeletonDebugScale`: how large to draw the live human skeleton.
- `BodyTargetScale`: torso/hand/foot target placement scale.
- `HeadTargetScale`: head/neck/face target scale.
- Optional per-limb scale: arm span can differ from torso height for stylized wings.

For the current saedaegal case:

- keep hand/wing reach separately tunable;
- keep head/neck target scale close to 1 unless explicitly calibrated;
- debug skeleton should default to raw mapped human meters or a separate debug scale, not avatar fitting scale.

### 2. Head Direction Priority

Head direction should use this order:

1. `Head -> Nose` position vector only if both joints meet confidence threshold.
2. Mapped `Head` orientation forward vector if head confidence is acceptable.
3. `Neck -> Head` only as a weak fallback, and only after projection/clamping because it is an up-axis relation, not a face direction.
4. Hold last stable direction if all sources are unreliable.

Never let low-confidence face joints snap the avatar head.

### 3. Bone Lengths Should Come From The Avatar

K4ABT joint distances are observations of a human body. They are not target bone lengths for a stylized mascot.

Correct behavior:

- Positions define control targets in a normalized station coordinate space.
- Rotations/directions define intent.
- Avatar mesh/bone lengths remain owned by the FBX rig.
- The retargeter applies constrained rotations and IK weights, not scale changes.

### 4. Debugger Must Show Both Raw And Retargeted Spaces

The debugger should make these visually distinct:

- raw DSCC skeleton, after coordinate mapping, in meters;
- retarget targets after avatar-fitting scale/offsets;
- actual avatar bones.

When a line looks stretched, the debugger should state whether it is raw sensor space, avatar target space, or actual bone space.

### 5. Confidence Gating Is Not Optional

For runtime body tracking, confidence must be part of every target update:

- medium/high confidence: update target normally;
- low confidence: use smoothing/hold-last for orientation-sensitive joints;
- none: do not update;
- face joints must have stricter gating than hands because small face-vector errors produce large head rotations.

## Immediate Code Direction

1. Add explicit scale fields to `DsccRetargetingProfile`:
   - `TargetScale` or keep `AvatarScale` as legacy body scale.
   - `HeadScale`.
   - `DebugSkeletonScale`.
2. Update `DsccAvatarRetargeter` so `HeadAim` uses `HeadScale`, not `AvatarScale`.
3. Update `DsccSkeletonDebugDrawer` and `DsccSkeletonSceneDebugger` so debug skeleton uses `DebugSkeletonScale` or raw meter scale.
4. Keep `DsccHeadBoneDriver` as a constrained direct-bone driver for stylized characters.
5. Add a calibration tool that captures:
   - avatar arm span;
   - avatar `DEF-neck -> DEF-head` rest distance;
   - live user `ShoulderLeft -> ShoulderRight`, `Neck -> Head`, `Head -> Nose`;
   - computed per-segment ratios.

## Files To Read Before Editing

Main DSCC sender:

- `src/DSCC.Orbbec/K4aBodyTrackingSkeletonSource.cs`
- `src/DSCC.Protocol/StationSkeletonFrame.cs`
- `src/DSCC.Protocol/JointFrameDto.cs`

Unity receiver/retargeting:

- `Assets/Tools/DSCC/Runtime/Retargeting/DsccCoordinateMapper.cs`
- `Assets/Tools/DSCC/Runtime/Retargeting/DsccRetargetingProfile.cs`
- `Assets/Tools/DSCC/Runtime/Retargeting/DsccAvatarRetargeter.cs`
- `Assets/Tools/DSCC/Runtime/Retargeting/DsccHeadBoneDriver.cs`
- `Assets/Tools/DSCC/Runtime/Debugging/DsccSkeletonDebugDrawer.cs`
- `Assets/Tools/DSCC/Editor/DsccSkeletonSceneDebugger.cs`

## Guardrail

Before changing retargeting code, answer these three checks in the implementation notes:

1. Which space is this value in: sensor/depth camera, station anchor, target, avatar bone local, or avatar bone world?
2. Which scale is being applied: raw meters, debug scale, body target scale, head scale, or per-limb calibrated scale?
3. What happens when the source joint confidence is low?
