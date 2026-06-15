namespace DSCC.Protocol;

public sealed class HeadRotationStabilizer
{
    private const float DefaultFrameDeltaSeconds = 1.0f / 15.0f;
    private static readonly HashSet<string> StabilizedJointNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Head",
        "Neck"
    };

    private readonly object syncRoot = new();
    private readonly Dictionary<int, StationState> stationStates = [];

    public StationSkeletonFrame Apply(StationSkeletonFrame frame, HeadRotationStabilizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled || frame.Joints.Length == 0)
        {
            return frame;
        }

        if (!frame.HasPlayer || frame.State is StationStateDto.Empty or StationStateDto.Exited or StationStateDto.Disabled)
        {
            Reset(frame.StationId);
            return frame;
        }

        lock (syncRoot)
        {
            return ApplyCore(frame, options);
        }
    }

    private StationSkeletonFrame ApplyCore(StationSkeletonFrame frame, HeadRotationStabilizerOptions options)
    {
        var state = GetOrCreateState(frame.StationId);
        var changed = false;
        var joints = new JointFrameDto[frame.Joints.Length];
        for (var index = 0; index < frame.Joints.Length; index++)
        {
            var joint = frame.Joints[index];
            if (joint is null)
            {
                changed = true;
                joints[index] = new JointFrameDto();
                continue;
            }

            if (!StabilizedJointNames.Contains(joint.Name))
            {
                joints[index] = joint;
                continue;
            }

            var rotation = StabilizeJointRotation(state, joint, frame.TimestampUsec, options);
            changed = changed || !NearlyEqual(rotation, joint.RotationLocal);
            joints[index] = new JointFrameDto
            {
                Name = joint.Name,
                PositionLocal = joint.PositionLocal,
                RotationLocal = rotation,
                Confidence = joint.Confidence
            };
        }

        return changed
            ? CopyWithJoints(frame, joints)
            : frame;
    }

    public void Reset()
    {
        lock (syncRoot)
        {
            stationStates.Clear();
        }
    }

    public void Reset(int stationId)
    {
        lock (syncRoot)
        {
            stationStates.Remove(stationId);
        }
    }

    private static StationSkeletonFrame CopyWithJoints(StationSkeletonFrame frame, JointFrameDto[] joints)
    {
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
            PelvisLocal = frame.PelvisLocal,
            BodyRotation = frame.BodyRotation,
            Joints = joints,
            AnchorPosition = frame.AnchorPosition,
            AnchorRotationYDegrees = frame.AnchorRotationYDegrees
        };
    }

    private StationState GetOrCreateState(int stationId)
    {
        if (!stationStates.TryGetValue(stationId, out var state))
        {
            state = new StationState();
            stationStates[stationId] = state;
        }

        return state;
    }

    private static QuaternionDto StabilizeJointRotation(
        StationState state,
        JointFrameDto joint,
        long timestampUsec,
        HeadRotationStabilizerOptions options)
    {
        var target = Normalize(joint.RotationLocal);
        if (!IsFinite(target))
        {
            return joint.RotationLocal;
        }

        var jointState = state.GetOrCreateJoint(joint.Name);
        var deltaSeconds = DeltaSeconds(jointState.LastTimestampUsec, timestampUsec);
        jointState.LastTimestampUsec = timestampUsec;

        if (!jointState.HasRotation)
        {
            jointState.Rotation = target;
            jointState.HasRotation = true;
            return target;
        }

        var previous = jointState.Rotation;
        if (Dot(previous, target) < 0.0f)
        {
            target = Negate(target);
        }

        var confidenceThreshold = Math.Clamp(options.MinConfidence, 0.0f, 1.0f);
        if (joint.Confidence < confidenceThreshold)
        {
            return previous;
        }

        var angleToTarget = AngleDegrees(previous, target);
        if (angleToTarget <= Math.Max(0.0f, options.DeadZoneDegrees))
        {
            return previous;
        }

        var smoothing = SmoothingFactor(options.SmoothingHalfLifeSeconds, deltaSeconds);
        var smoothed = Slerp(previous, target, smoothing);
        var angleToSmoothed = AngleDegrees(previous, smoothed);
        var maxStep = Math.Max(0.0f, options.MaxDegreesPerSecond) * deltaSeconds;
        if (maxStep > 0.0f && angleToSmoothed > maxStep)
        {
            smoothed = Slerp(previous, smoothed, maxStep / angleToSmoothed);
        }

        jointState.Rotation = Normalize(smoothed);
        return jointState.Rotation;
    }

    private static float DeltaSeconds(long previousTimestampUsec, long timestampUsec)
    {
        if (previousTimestampUsec <= 0 || timestampUsec <= previousTimestampUsec)
        {
            return DefaultFrameDeltaSeconds;
        }

        var seconds = (timestampUsec - previousTimestampUsec) / 1_000_000.0f;
        return Math.Clamp(seconds, 1.0f / 240.0f, 0.5f);
    }

    private static float SmoothingFactor(float halfLifeSeconds, float deltaSeconds)
    {
        if (halfLifeSeconds <= 0.0f)
        {
            return 1.0f;
        }

        return Math.Clamp(1.0f - MathF.Pow(0.5f, deltaSeconds / halfLifeSeconds), 0.0f, 1.0f);
    }

    private static QuaternionDto Slerp(QuaternionDto from, QuaternionDto to, float amount)
    {
        amount = Math.Clamp(amount, 0.0f, 1.0f);
        var dot = Dot(from, to);
        if (dot < 0.0f)
        {
            to = Negate(to);
            dot = -dot;
        }

        dot = Math.Clamp(dot, -1.0f, 1.0f);
        if (dot > 0.9995f)
        {
            return Normalize(new QuaternionDto(
                from.X + amount * (to.X - from.X),
                from.Y + amount * (to.Y - from.Y),
                from.Z + amount * (to.Z - from.Z),
                from.W + amount * (to.W - from.W)));
        }

        var theta0 = MathF.Acos(dot);
        var theta = theta0 * amount;
        var sinTheta = MathF.Sin(theta);
        var sinTheta0 = MathF.Sin(theta0);
        var scaleFrom = MathF.Cos(theta) - dot * sinTheta / sinTheta0;
        var scaleTo = sinTheta / sinTheta0;

        return Normalize(new QuaternionDto(
            scaleFrom * from.X + scaleTo * to.X,
            scaleFrom * from.Y + scaleTo * to.Y,
            scaleFrom * from.Z + scaleTo * to.Z,
            scaleFrom * from.W + scaleTo * to.W));
    }

    private static float AngleDegrees(QuaternionDto from, QuaternionDto to)
    {
        var dot = Math.Abs(Math.Clamp(Dot(Normalize(from), Normalize(to)), -1.0f, 1.0f));
        return 2.0f * MathF.Acos(dot) * 180.0f / MathF.PI;
    }

    private static QuaternionDto Normalize(QuaternionDto value)
    {
        var lengthSquared = value.X * value.X + value.Y * value.Y + value.Z * value.Z + value.W * value.W;
        if (lengthSquared <= 0.000001f || !float.IsFinite(lengthSquared))
        {
            return QuaternionDto.Identity;
        }

        var scale = 1.0f / MathF.Sqrt(lengthSquared);
        return new QuaternionDto(value.X * scale, value.Y * scale, value.Z * scale, value.W * scale);
    }

    private static QuaternionDto Negate(QuaternionDto value)
    {
        return new QuaternionDto(-value.X, -value.Y, -value.Z, -value.W);
    }

    private static float Dot(QuaternionDto left, QuaternionDto right)
    {
        return left.X * right.X + left.Y * right.Y + left.Z * right.Z + left.W * right.W;
    }

    private static bool IsFinite(QuaternionDto value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z) &&
               float.IsFinite(value.W);
    }

    private static bool NearlyEqual(QuaternionDto left, QuaternionDto right)
    {
        const float tolerance = 0.0001f;
        return Math.Abs(left.X - right.X) <= tolerance &&
               Math.Abs(left.Y - right.Y) <= tolerance &&
               Math.Abs(left.Z - right.Z) <= tolerance &&
               Math.Abs(left.W - right.W) <= tolerance;
    }

    private sealed class StationState
    {
        private readonly Dictionary<string, JointState> joints = new(StringComparer.OrdinalIgnoreCase);

        public JointState GetOrCreateJoint(string jointName)
        {
            if (!joints.TryGetValue(jointName, out var joint))
            {
                joint = new JointState();
                joints[jointName] = joint;
            }

            return joint;
        }
    }

    private sealed class JointState
    {
        public bool HasRotation { get; set; }

        public QuaternionDto Rotation { get; set; } = QuaternionDto.Identity;

        public long LastTimestampUsec { get; set; }
    }
}
