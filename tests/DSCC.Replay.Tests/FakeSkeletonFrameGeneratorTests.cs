using DSCC.Core.Diagnostics;
using DSCC.Protocol;
using DSCC.Replay;

namespace DSCC.Replay.Tests;

public sealed class FakeSkeletonFrameGeneratorTests
{
    private static readonly string[] K4abtJointNames =
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

    [Fact]
    public void CreateSequence_ActiveFrameContainsFullK4abtJointSet()
    {
        var generator = new FakeSkeletonFrameGenerator();

        var frame = generator
            .CreateSequence(new FakeSkeletonFrameOptions
            {
                Fps = 30,
                WarmupOutsideDuration = TimeSpan.Zero,
                EnterDuration = TimeSpan.Zero,
                ActiveDuration = TimeSpan.FromSeconds(1),
                LostDuration = TimeSpan.Zero,
                ExitedDuration = TimeSpan.Zero,
                EmptyTailDuration = TimeSpan.Zero
            })
            .First(candidate => candidate.State == StationStateDto.Active);

        Assert.True(frame.HasPlayer);
        Assert.Equal(1, frame.BodyCount);
        Assert.Equal(1, frame.SelectedBodyId);
        Assert.Equal(K4abtJointNames, frame.Joints.Select(joint => joint.Name).ToArray());
        Assert.All(frame.Joints, joint => Assert.Equal(0.92f, joint.Confidence));
    }

    [Fact]
    public void CreateSequence_ExtraHandAndFaceJointsAreOffsetFromPelvis()
    {
        var generator = new FakeSkeletonFrameGenerator();

        var frame = generator
            .CreateSequence(new FakeSkeletonFrameOptions
            {
                Fps = 1,
                WarmupOutsideDuration = TimeSpan.Zero,
                EnterDuration = TimeSpan.Zero,
                ActiveDuration = TimeSpan.FromSeconds(1),
                LostDuration = TimeSpan.Zero,
                ExitedDuration = TimeSpan.Zero,
                EmptyTailDuration = TimeSpan.Zero,
                ActiveSwayMeters = 0
            })
            .Single();

        var joints = frame.Joints.ToDictionary(joint => joint.Name);

        Assert.True(joints["HandTipLeft"].PositionLocal.X < joints["WristLeft"].PositionLocal.X);
        Assert.True(joints["HandTipRight"].PositionLocal.X > joints["WristRight"].PositionLocal.X);
        Assert.True(joints["Nose"].PositionLocal.Z > joints["Head"].PositionLocal.Z);
        Assert.True(joints["EyeLeft"].PositionLocal.X < joints["EyeRight"].PositionLocal.X);
        Assert.True(joints["EarLeft"].PositionLocal.X < joints["EarRight"].PositionLocal.X);
    }

    [Fact]
    public void CreateSequence_ActiveFramesAnimateLimbsAndHeadForAvatarSmoke()
    {
        var generator = new FakeSkeletonFrameGenerator();

        var frames = generator
            .CreateSequence(new FakeSkeletonFrameOptions
            {
                Fps = 30,
                WarmupOutsideDuration = TimeSpan.Zero,
                EnterDuration = TimeSpan.Zero,
                ActiveDuration = TimeSpan.FromSeconds(2),
                LostDuration = TimeSpan.Zero,
                ExitedDuration = TimeSpan.Zero,
                EmptyTailDuration = TimeSpan.Zero,
                ActiveSwayMeters = 0
            });

        var first = frames[0].Joints.ToDictionary(joint => joint.Name);
        var later = frames[15].Joints.ToDictionary(joint => joint.Name);

        Assert.True(later["WristLeft"].PositionLocal.Y > first["WristLeft"].PositionLocal.Y);
        Assert.NotEqual(first["WristLeft"].PositionLocal.Z, later["WristLeft"].PositionLocal.Z);
        Assert.NotEqual(first["KneeLeft"].PositionLocal.Z, later["KneeLeft"].PositionLocal.Z);
        Assert.NotEqual(first["Nose"].PositionLocal.X, later["Nose"].PositionLocal.X);
    }

    [Fact]
    public void CreateSequence_ActiveFramesSatisfyFieldAppDrivingJointGate()
    {
        var generator = new FakeSkeletonFrameGenerator();

        var activeFrames = generator
            .CreateSequence(new FakeSkeletonFrameOptions
            {
                Fps = 30,
                WarmupOutsideDuration = TimeSpan.Zero,
                EnterDuration = TimeSpan.Zero,
                ActiveDuration = TimeSpan.FromSeconds(2),
                LostDuration = TimeSpan.Zero,
                ExitedDuration = TimeSpan.Zero,
                EmptyTailDuration = TimeSpan.Zero,
                ActiveSwayMeters = 0
            })
            .Where(frame => frame.State == StationStateDto.Active)
            .ToArray();

        Assert.NotEmpty(activeFrames);

        foreach (var jointName in FieldSkeletonAcceptancePolicy.AppDrivingJointNames)
        {
            var pelvisRelativeSamples = activeFrames
                .Select(frame =>
                {
                    var joint = frame.Joints.Single(candidate => candidate.Name == jointName);
                    Assert.True(joint.Confidence >= 0.8f);
                    return new Vector3Dto(
                        joint.PositionLocal.X - frame.PelvisLocal.X,
                        joint.PositionLocal.Y - frame.PelvisLocal.Y,
                        joint.PositionLocal.Z - frame.PelvisLocal.Z);
                })
                .ToArray();

            var motionMeters = CalculateRangeMeters(pelvisRelativeSamples);

            Assert.True(
                motionMeters >= FieldSkeletonAcceptancePolicy.DefaultMotionThresholdMeters,
                $"{jointName} pelvis-relative motion {motionMeters:0.000}m was below field smoke threshold.");
        }
    }

    [Fact]
    public void CreateSequence_CanDisableJointAnimationForStaticPoseBaselines()
    {
        var generator = new FakeSkeletonFrameGenerator();

        var frames = generator
            .CreateSequence(new FakeSkeletonFrameOptions
            {
                Fps = 30,
                WarmupOutsideDuration = TimeSpan.Zero,
                EnterDuration = TimeSpan.Zero,
                ActiveDuration = TimeSpan.FromSeconds(1),
                LostDuration = TimeSpan.Zero,
                ExitedDuration = TimeSpan.Zero,
                EmptyTailDuration = TimeSpan.Zero,
                ActiveSwayMeters = 0,
                AnimateJoints = false
            });

        var first = frames[0].Joints.ToDictionary(joint => joint.Name);
        var later = frames[15].Joints.ToDictionary(joint => joint.Name);

        Assert.Equal(first["WristLeft"].PositionLocal, later["WristLeft"].PositionLocal);
        Assert.Equal(first["KneeLeft"].PositionLocal, later["KneeLeft"].PositionLocal);
        Assert.Equal(first["Nose"].PositionLocal, later["Nose"].PositionLocal);
    }

    [Fact]
    public void UnityBoundTransforms_PreserveFullJointSetAndSwapLeftRightNames()
    {
        var generator = new FakeSkeletonFrameGenerator();
        var source = generator
            .CreateSequence(new FakeSkeletonFrameOptions
            {
                Fps = 1,
                WarmupOutsideDuration = TimeSpan.Zero,
                EnterDuration = TimeSpan.Zero,
                ActiveDuration = TimeSpan.FromSeconds(1),
                LostDuration = TimeSpan.Zero,
                ExitedDuration = TimeSpan.Zero,
                EmptyTailDuration = TimeSpan.Zero,
                ActiveSwayMeters = 0
            })
            .Single();

        var actual = SkeletonFrameTransforms.MirrorPerformerFacingCamera(
            ReplayFrameConventions.ToK4aCameraConvention(source));

        Assert.Equal(32, actual.Joints.Length);
        Assert.Equal(source.BodyCount, actual.BodyCount);
        Assert.Equal(source.SelectedBodyId, actual.SelectedBodyId);
        Assert.Contains(actual.Joints, joint => joint.Name == "HandTipRight");
        Assert.Contains(actual.Joints, joint => joint.Name == "ThumbRight");
        Assert.Contains(actual.Joints, joint => joint.Name == "EyeRight");
        Assert.Contains(actual.Joints, joint => joint.Name == "EarRight");

        var sourceLeftHandTip = source.Joints.Single(joint => joint.Name == "HandTipLeft");
        var actualRightHandTip = actual.Joints.Single(joint => joint.Name == "HandTipRight");
        Assert.Equal(sourceLeftHandTip.PositionLocal.X, actualRightHandTip.PositionLocal.X);
        Assert.Equal(-sourceLeftHandTip.PositionLocal.Y, actualRightHandTip.PositionLocal.Y);
        Assert.Equal(sourceLeftHandTip.PositionLocal.Z, actualRightHandTip.PositionLocal.Z);
    }

    private static double CalculateRangeMeters(IReadOnlyList<Vector3Dto> samples)
    {
        Assert.NotEmpty(samples);

        var minX = samples.Min(sample => sample.X);
        var maxX = samples.Max(sample => sample.X);
        var minY = samples.Min(sample => sample.Y);
        var maxY = samples.Max(sample => sample.Y);
        var minZ = samples.Min(sample => sample.Z);
        var maxZ = samples.Max(sample => sample.Z);

        var dx = maxX - minX;
        var dy = maxY - minY;
        var dz = maxZ - minZ;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
