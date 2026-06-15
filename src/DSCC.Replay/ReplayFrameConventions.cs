using DSCC.Protocol;

namespace DSCC.Replay;

/// <summary>
/// <see cref="FakeSkeletonFrameGenerator"/> emits stage-space frames: Y up and the
/// performer's left side on -X (a performer facing away from the camera). Live K4A
/// frames are depth-camera space: Y down and a camera-facing performer's left on +X.
/// The Unity retargeting profiles are calibrated against the live convention, so any
/// replay frame bound for Unity must pass through this conversion before the usual
/// Unity-bound transforms (head stabilizer, MirrorPerformerFacingCamera).
/// </summary>
public static class ReplayFrameConventions
{
    public static StationSkeletonFrame ToK4aCameraConvention(StationSkeletonFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return new StationSkeletonFrame
        {
            ProtocolVersion = frame.ProtocolVersion,
            StationId = frame.StationId,
            CameraSerial = frame.CameraSerial,
            DeviceType = frame.DeviceType,
            TimestampUsec = frame.TimestampUsec,
            HasPlayer = frame.HasPlayer,
            State = frame.State,
            Confidence = frame.Confidence,
            IsInsideFootMarker = frame.IsInsideFootMarker,
            IsInsideTrackingRoi = frame.IsInsideTrackingRoi,
            TrackingLostSeconds = frame.TrackingLostSeconds,
            PelvisLocal = NegateXY(frame.PelvisLocal),
            BodyRotation = frame.BodyRotation,
            Joints = frame.Joints
                .Select(joint => new JointFrameDto
                {
                    Name = joint.Name,
                    PositionLocal = NegateXY(joint.PositionLocal),
                    RotationLocal = joint.RotationLocal,
                    Confidence = joint.Confidence
                })
                .ToArray(),
            AnchorPosition = frame.AnchorPosition,
            AnchorRotationYDegrees = frame.AnchorRotationYDegrees
        };
    }

    private static Vector3Dto NegateXY(Vector3Dto value)
    {
        return new Vector3Dto(-value.X, -value.Y, value.Z);
    }
}
