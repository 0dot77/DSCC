namespace DSCC.Protocol;

public static class SkeletonFrameTransforms
{
    public static StationSkeletonFrame MirrorPerformerFacingCamera(StationSkeletonFrame frame)
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
            PelvisLocal = MirrorVectorX(frame.PelvisLocal),
            BodyRotation = MirrorQuaternionX(frame.BodyRotation),
            Joints = frame.Joints
                .Select(MirrorJointX)
                .ToArray(),
            // The anchor is a Unity-space constant, not sensor-space data; it
            // is not part of the mirror.
            AnchorPosition = frame.AnchorPosition,
            AnchorRotationYDegrees = frame.AnchorRotationYDegrees
        };
    }

    private static JointFrameDto MirrorJointX(JointFrameDto joint)
    {
        return new JointFrameDto
        {
            Name = SwapLeftRight(joint.Name),
            PositionLocal = MirrorVectorX(joint.PositionLocal),
            RotationLocal = MirrorQuaternionX(joint.RotationLocal),
            Confidence = joint.Confidence
        };
    }

    private static Vector3Dto MirrorVectorX(Vector3Dto value)
    {
        return new Vector3Dto(-value.X, value.Y, value.Z);
    }

    private static QuaternionDto MirrorQuaternionX(QuaternionDto value)
    {
        return new QuaternionDto(value.X, -value.Y, -value.Z, value.W);
    }

    private static string SwapLeftRight(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value
            .Replace("Left", "__DSCC_LEFT__", StringComparison.Ordinal)
            .Replace("Right", "Left", StringComparison.Ordinal)
            .Replace("__DSCC_LEFT__", "Right", StringComparison.Ordinal);
    }
}
