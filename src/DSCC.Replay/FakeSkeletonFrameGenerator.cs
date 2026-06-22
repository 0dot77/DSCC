using DSCC.Protocol;

namespace DSCC.Replay;

public sealed class FakeSkeletonFrameGenerator
{
    private static readonly string[] JointNames =
    [
        "Pelvis",
        "SpineNavel",
        "SpineChest",
        "Neck",
        "ClavicleLeft",
        "ShoulderLeft",
        "ElbowLeft",
        "WristLeft",
        "HandLeft",
        "HandTipLeft",
        "ThumbLeft",
        "ClavicleRight",
        "ShoulderRight",
        "ElbowRight",
        "WristRight",
        "HandRight",
        "HandTipRight",
        "ThumbRight",
        "HipLeft",
        "KneeLeft",
        "AnkleLeft",
        "FootLeft",
        "HipRight",
        "KneeRight",
        "AnkleRight",
        "FootRight",
        "Head",
        "Nose",
        "EyeLeft",
        "EarLeft",
        "EyeRight",
        "EarRight"
    ];

    public IReadOnlyList<StationSkeletonFrame> CreateSequence(FakeSkeletonFrameOptions? options = null)
    {
        options ??= new FakeSkeletonFrameOptions();
        options.Validate();

        var frameCount = Math.Max(1, (int)Math.Ceiling(options.Duration.TotalSeconds * options.Fps));
        var frames = new List<StationSkeletonFrame>(frameCount);
        var frameIntervalUsec = (long)Math.Round(1_000_000d / options.Fps);
        var startTimestampUsec = options.StartTimestampUsec ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000;

        for (var index = 0; index < frameCount; index++)
        {
            var elapsed = TimeSpan.FromSeconds(index / (double)options.Fps);
            var pelvis = CalculatePelvis(elapsed, options);
            var insideRoi = options.TrackingRoi.Contains(pelvis);
            var insideFootMarker = IsInsideFootMarker(pelvis, options);
            var state = CalculateState(elapsed, options);
            var hasPlayer = state is StationStateDto.Entering or StationStateDto.Active;
            var confidence = hasPlayer ? options.InsideConfidence : options.OutsideConfidence;

            frames.Add(new StationSkeletonFrame
            {
                StationId = options.StationId,
                CameraSerial = options.CameraSerial,
                DeviceType = options.DeviceType,
                TimestampUsec = startTimestampUsec + index * frameIntervalUsec,
                HasPlayer = hasPlayer,
                State = state,
                Confidence = confidence,
                IsInsideFootMarker = insideFootMarker,
                IsInsideTrackingRoi = insideRoi,
                TrackingLostSeconds = CalculateLostSeconds(elapsed, options),
                PelvisLocal = pelvis,
                BodyRotation = QuaternionDto.Identity,
                Joints = CreateJoints(pelvis, confidence, CalculateJointMotion(elapsed, hasPlayer, options)),
                BodyCount = hasPlayer ? 1 : 0,
                SelectedBodyId = hasPlayer ? options.StationId : -1
            });
        }

        return frames;
    }

    public async IAsyncEnumerable<StationSkeletonFrame> PlaySequenceAsync(
        FakeSkeletonFrameOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new FakeSkeletonFrameOptions();
        var frames = CreateSequence(options);
        var frameDelay = TimeSpan.FromSeconds(1d / options.Fps);

        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return frame;
            await Task.Delay(frameDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Vector3Dto CalculatePelvis(TimeSpan elapsed, FakeSkeletonFrameOptions options)
    {
        var seconds = elapsed.TotalSeconds;
        var warmupEnd = options.WarmupOutsideDuration.TotalSeconds;
        var enterEnd = warmupEnd + options.EnterDuration.TotalSeconds;
        var activeEnd = enterEnd + options.ActiveDuration.TotalSeconds;
        var lostEnd = activeEnd + options.LostDuration.TotalSeconds;

        if (seconds < warmupEnd)
        {
            return options.OutsideLeftPelvis;
        }

        if (seconds < enterEnd)
        {
            var t = Normalize(seconds, warmupEnd, enterEnd);
            return Lerp(options.OutsideLeftPelvis, options.InsidePelvis, SmoothStep(t));
        }

        if (seconds < activeEnd)
        {
            var phase = (seconds - enterEnd) * Math.Tau * 0.5;
            var swayX = (float)(Math.Sin(phase) * options.ActiveSwayMeters);
            var swayZ = (float)(Math.Cos(phase) * options.ActiveSwayMeters * 0.35);
            return new Vector3Dto(
                options.InsidePelvis.X + swayX,
                options.InsidePelvis.Y,
                options.InsidePelvis.Z + swayZ);
        }

        if (seconds < lostEnd)
        {
            var t = Normalize(seconds, activeEnd, lostEnd);
            return Lerp(options.InsidePelvis, options.OutsideRightPelvis, SmoothStep(t));
        }

        return options.OutsideRightPelvis;
    }

    private static StationStateDto CalculateState(TimeSpan elapsed, FakeSkeletonFrameOptions options)
    {
        var seconds = elapsed.TotalSeconds;
        var warmupEnd = options.WarmupOutsideDuration.TotalSeconds;
        var enterEnd = warmupEnd + options.EnterDuration.TotalSeconds;
        var activeEnd = enterEnd + options.ActiveDuration.TotalSeconds;
        var lostEnd = activeEnd + options.LostDuration.TotalSeconds;
        var exitedEnd = lostEnd + options.ExitedDuration.TotalSeconds;

        if (seconds < warmupEnd)
        {
            return StationStateDto.Empty;
        }

        if (seconds < enterEnd)
        {
            return StationStateDto.Entering;
        }

        if (seconds < activeEnd)
        {
            return StationStateDto.Active;
        }

        if (seconds < lostEnd)
        {
            return StationStateDto.Lost;
        }

        return seconds < exitedEnd ? StationStateDto.Exited : StationStateDto.Empty;
    }

    private static float CalculateLostSeconds(TimeSpan elapsed, FakeSkeletonFrameOptions options)
    {
        var activeEnd = options.WarmupOutsideDuration + options.EnterDuration + options.ActiveDuration;

        if (elapsed <= activeEnd)
        {
            return 0;
        }

        return (float)(elapsed - activeEnd).TotalSeconds;
    }

    private static JointFrameDto[] CreateJoints(
        Vector3Dto pelvis,
        float confidence,
        FakeSkeletonJointMotion motion)
    {
        var joints = new JointFrameDto[JointNames.Length];

        for (var index = 0; index < JointNames.Length; index++)
        {
            joints[index] = new JointFrameDto
            {
                Name = JointNames[index],
                PositionLocal = OffsetJoint(JointNames[index], pelvis, motion),
                RotationLocal = QuaternionDto.Identity,
                Confidence = confidence
            };
        }

        return joints;
    }

    private static FakeSkeletonJointMotion CalculateJointMotion(
        TimeSpan elapsed,
        bool hasPlayer,
        FakeSkeletonFrameOptions options)
    {
        if (!hasPlayer || !options.AnimateJoints)
        {
            return FakeSkeletonJointMotion.Neutral;
        }

        var phase = (float)(elapsed.TotalSeconds * Math.PI * 2d * options.MotionCyclesPerSecond);
        var oppositePhase = phase + MathF.PI;
        var limb = options.LimbMotionMeters;
        var head = options.HeadMotionMeters;
        var leftStep = MathF.Sin(phase);
        var rightStep = MathF.Sin(oppositePhase);

        return new FakeSkeletonJointMotion(
            TorsoLeanX: MathF.Sin(phase * 0.5f) * limb * 0.08f,
            HeadLookX: MathF.Sin(phase * 0.5f) * head,
            LeftArmLift: (1f - MathF.Cos(phase)) * limb * 0.5f,
            RightArmLift: (1f - MathF.Cos(oppositePhase)) * limb * 0.5f,
            LeftArmForward: leftStep * limb * 0.35f,
            RightArmForward: rightStep * limb * 0.35f,
            LeftLegForward: leftStep * limb * 0.35f,
            RightLegForward: rightStep * limb * 0.35f,
            LeftKneeLift: MathF.Max(0, leftStep) * limb * 0.42f,
            RightKneeLift: MathF.Max(0, rightStep) * limb * 0.42f);
    }

    private static Vector3Dto OffsetJoint(string jointName, Vector3Dto pelvis, FakeSkeletonJointMotion motion)
    {
        return jointName switch
        {
            "SpineNavel" => pelvis with { Y = pelvis.Y + 0.25f },
            "SpineChest" => new Vector3Dto(pelvis.X + motion.TorsoLeanX * 0.4f, pelvis.Y + 0.55f, pelvis.Z),
            "Neck" => new Vector3Dto(pelvis.X + motion.TorsoLeanX * 0.75f, pelvis.Y + 0.82f, pelvis.Z),
            "Head" => new Vector3Dto(pelvis.X + motion.TorsoLeanX + motion.HeadLookX * 0.45f, pelvis.Y + 1.05f, pelvis.Z),
            "ClavicleLeft" => new Vector3Dto(pelvis.X - 0.12f + motion.TorsoLeanX * 0.55f, pelvis.Y + 0.72f, pelvis.Z),
            "ShoulderLeft" => new Vector3Dto(pelvis.X - 0.28f + motion.TorsoLeanX * 0.55f, pelvis.Y + 0.68f, pelvis.Z),
            "ElbowLeft" => new Vector3Dto(pelvis.X - 0.48f + motion.TorsoLeanX * 0.45f, pelvis.Y + 0.45f + motion.LeftArmLift * 0.55f, pelvis.Z + motion.LeftArmForward * 0.55f),
            "WristLeft" => new Vector3Dto(pelvis.X - 0.58f + motion.TorsoLeanX * 0.35f, pelvis.Y + 0.22f + motion.LeftArmLift, pelvis.Z + motion.LeftArmForward),
            "HandLeft" => new Vector3Dto(pelvis.X - 0.62f + motion.TorsoLeanX * 0.35f, pelvis.Y + 0.16f + motion.LeftArmLift, pelvis.Z + 0.02f + motion.LeftArmForward),
            "HandTipLeft" => new Vector3Dto(pelvis.X - 0.66f + motion.TorsoLeanX * 0.35f, pelvis.Y + 0.10f + motion.LeftArmLift, pelvis.Z + 0.04f + motion.LeftArmForward),
            "ThumbLeft" => new Vector3Dto(pelvis.X - 0.62f + motion.TorsoLeanX * 0.35f, pelvis.Y + 0.12f + motion.LeftArmLift * 0.9f, pelvis.Z - 0.05f + motion.LeftArmForward),
            "ClavicleRight" => new Vector3Dto(pelvis.X + 0.12f + motion.TorsoLeanX * 0.55f, pelvis.Y + 0.72f, pelvis.Z),
            "ShoulderRight" => new Vector3Dto(pelvis.X + 0.28f + motion.TorsoLeanX * 0.55f, pelvis.Y + 0.68f, pelvis.Z),
            "ElbowRight" => new Vector3Dto(pelvis.X + 0.48f + motion.TorsoLeanX * 0.45f, pelvis.Y + 0.45f + motion.RightArmLift * 0.55f, pelvis.Z + motion.RightArmForward * 0.55f),
            "WristRight" => new Vector3Dto(pelvis.X + 0.58f + motion.TorsoLeanX * 0.35f, pelvis.Y + 0.22f + motion.RightArmLift, pelvis.Z + motion.RightArmForward),
            "HandRight" => new Vector3Dto(pelvis.X + 0.62f + motion.TorsoLeanX * 0.35f, pelvis.Y + 0.16f + motion.RightArmLift, pelvis.Z + 0.02f + motion.RightArmForward),
            "HandTipRight" => new Vector3Dto(pelvis.X + 0.66f + motion.TorsoLeanX * 0.35f, pelvis.Y + 0.10f + motion.RightArmLift, pelvis.Z + 0.04f + motion.RightArmForward),
            "ThumbRight" => new Vector3Dto(pelvis.X + 0.62f + motion.TorsoLeanX * 0.35f, pelvis.Y + 0.12f + motion.RightArmLift * 0.9f, pelvis.Z - 0.05f + motion.RightArmForward),
            "HipLeft" => new Vector3Dto(pelvis.X - 0.12f, pelvis.Y - 0.05f, pelvis.Z),
            "KneeLeft" => new Vector3Dto(pelvis.X - 0.16f, pelvis.Y - 0.48f + motion.LeftKneeLift, pelvis.Z + 0.03f + motion.LeftLegForward * 0.65f),
            "AnkleLeft" => new Vector3Dto(pelvis.X - 0.17f, pelvis.Y - 0.88f + motion.LeftKneeLift * 0.35f, pelvis.Z + motion.LeftLegForward),
            "FootLeft" => new Vector3Dto(pelvis.X - 0.17f, pelvis.Y - 0.9f + motion.LeftKneeLift * 0.25f, pelvis.Z + 0.12f + motion.LeftLegForward),
            "HipRight" => new Vector3Dto(pelvis.X + 0.12f, pelvis.Y - 0.05f, pelvis.Z),
            "KneeRight" => new Vector3Dto(pelvis.X + 0.16f, pelvis.Y - 0.48f + motion.RightKneeLift, pelvis.Z - 0.03f + motion.RightLegForward * 0.65f),
            "AnkleRight" => new Vector3Dto(pelvis.X + 0.17f, pelvis.Y - 0.88f + motion.RightKneeLift * 0.35f, pelvis.Z + motion.RightLegForward),
            "FootRight" => new Vector3Dto(pelvis.X + 0.17f, pelvis.Y - 0.9f + motion.RightKneeLift * 0.25f, pelvis.Z + 0.12f + motion.RightLegForward),
            "Nose" => new Vector3Dto(pelvis.X + motion.TorsoLeanX + motion.HeadLookX, pelvis.Y + 1.02f, pelvis.Z + 0.12f),
            "EyeLeft" => new Vector3Dto(pelvis.X - 0.04f + motion.TorsoLeanX + motion.HeadLookX * 0.85f, pelvis.Y + 1.08f, pelvis.Z + 0.09f),
            "EarLeft" => new Vector3Dto(pelvis.X - 0.11f + motion.TorsoLeanX + motion.HeadLookX * 0.65f, pelvis.Y + 1.06f, pelvis.Z + 0.01f),
            "EyeRight" => new Vector3Dto(pelvis.X + 0.04f + motion.TorsoLeanX + motion.HeadLookX * 0.85f, pelvis.Y + 1.08f, pelvis.Z + 0.09f),
            "EarRight" => new Vector3Dto(pelvis.X + 0.11f + motion.TorsoLeanX + motion.HeadLookX * 0.65f, pelvis.Y + 1.06f, pelvis.Z + 0.01f),
            _ => pelvis
        };
    }

    private static bool IsInsideFootMarker(Vector3Dto pelvis, FakeSkeletonFrameOptions options)
    {
        var dx = pelvis.X - options.FootMarkerCenter.X;
        var dz = pelvis.Z - options.FootMarkerCenter.Z;
        return Math.Sqrt(dx * dx + dz * dz) <= options.FootMarkerRadiusMeters;
    }

    private static Vector3Dto Lerp(Vector3Dto start, Vector3Dto end, double t)
    {
        return new Vector3Dto(
            (float)(start.X + (end.X - start.X) * t),
            (float)(start.Y + (end.Y - start.Y) * t),
            (float)(start.Z + (end.Z - start.Z) * t));
    }

    private static double Normalize(double value, double min, double max)
    {
        if (max <= min)
        {
            return 1;
        }

        return Math.Clamp((value - min) / (max - min), 0, 1);
    }

    private static double SmoothStep(double t)
    {
        return t * t * (3 - 2 * t);
    }

    private readonly record struct FakeSkeletonJointMotion(
        float TorsoLeanX,
        float HeadLookX,
        float LeftArmLift,
        float RightArmLift,
        float LeftArmForward,
        float RightArmForward,
        float LeftLegForward,
        float RightLegForward,
        float LeftKneeLift,
        float RightKneeLift)
    {
        public static FakeSkeletonJointMotion Neutral { get; } = new();
    }
}
