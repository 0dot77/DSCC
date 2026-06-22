using DSCC.Core.Diagnostics;

namespace DSCC.Core.Tests;

public sealed class FieldSkeletonAcceptancePolicyTests
{
    [Fact]
    public void AppDrivingJointNames_MatchFieldAvatarGate()
    {
        Assert.Equal(
            [
                "Head",
                "Nose",
                "HandLeft",
                "HandRight",
                "KneeLeft",
                "KneeRight",
                "AnkleLeft",
                "AnkleRight",
                "FootLeft",
                "FootRight"
            ],
            FieldSkeletonAcceptancePolicy.AppDrivingJointNames);
    }

    [Fact]
    public void FieldScripts_UseSharedAppDrivingJointCsv()
    {
        var repoRoot = FindRepoRoot();
        var expected = FieldSkeletonAcceptancePolicy.AppDrivingJointNamesCsv;
        var files = new[]
        {
            "tools/DsccUdpProbe/Program.cs",
            "tools/Get-FieldReadinessReport.ps1",
            "tools/Run-FieldReplaySmoke.ps1",
            "tools/Run-FieldSmoke.ps1",
            "tools/Run-TauriAppSmoke.ps1",
            "spec/Field_Udp_Smoke_Test.md"
        };

        foreach (var relativePath in files)
        {
            var path = Path.Combine(repoRoot, relativePath);
            Assert.True(File.Exists(path), $"Expected field acceptance file does not exist: {relativePath}");

            var text = File.ReadAllText(path);
            Assert.Contains(expected, text);
        }
    }

    [Fact]
    public void FieldRigPreflightScripts_RequireSeparateWristAndHandTargetBones()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            "tools/Get-FieldReadinessReport.ps1",
            "tools/Run-FieldSmoke.ps1"
        };

        foreach (var relativePath in files)
        {
            var path = Path.Combine(repoRoot, relativePath);
            Assert.True(File.Exists(path), $"Expected field rig preflight file does not exist: {relativePath}");

            var text = File.ReadAllText(path);
            Assert.Contains("WristLeft", text);
            Assert.Contains("WristRight", text);
            Assert.Contains("HandLeft", text);
            Assert.Contains("HandRight", text);
            Assert.Contains("lefthandend", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("righthandend", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("defpalm01l", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("defpalm01r", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FieldRigPreflightScripts_CheckTauriStageSceneRetargeterContract()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            "tools/Get-FieldReadinessReport.ps1",
            "tools/Run-FieldSmoke.ps1"
        };

        foreach (var relativePath in files)
        {
            var path = Path.Combine(repoRoot, relativePath);
            Assert.True(File.Exists(path), $"Expected field rig preflight file does not exist: {relativePath}");

            var text = File.ReadAllText(path);
            Assert.Contains("Add-TauriStageSceneRetargeterFailures", text);
            Assert.Contains("StageScene.tsx", text);
            Assert.Contains("Head->Nose retarget segment", text);
            Assert.Contains("Head->Nose confidence gate", text);
            Assert.Contains("debug segment WristLeft->HandLeft", text);
            Assert.Contains("debug segment AnkleLeft->FootLeft", text);
        }
    }

    [Fact]
    public void FieldSmokeSpec_DocumentsTauriStageSceneRetargeterContract()
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "spec/Field_Udp_Smoke_Test.md");
        Assert.True(File.Exists(path), "Expected field smoke spec does not exist.");

        var text = File.ReadAllText(path);
        Assert.Contains("StageScene.tsx", text);
        Assert.Contains("Head -> Nose", text);
        Assert.Contains("WristLeft -> HandLeft", text);
        Assert.Contains("AnkleLeft -> FootLeft", text);
        Assert.Contains("confidence gate", text);
        Assert.Contains("wrist/hand target bone", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FieldConfigValidation_RequiresFourMegaSyncRoleContract()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            "tools/DsccUdpProbe/Program.cs",
            "tools/Get-FieldReadinessReport.ps1",
            "spec/Field_Udp_Smoke_Test.md"
        };

        foreach (var relativePath in files)
        {
            var path = Path.Combine(repoRoot, relativePath);
            Assert.True(File.Exists(path), $"Expected field sync-role contract file does not exist: {relativePath}");

            var text = File.ReadAllText(path);
            Assert.Contains("syncRole", text);
            Assert.Contains("Primary", text);
            Assert.Contains("Secondary", text);
        }
    }

    [Fact]
    public void FieldSerialPinning_DocumentsDryRunDirectoryRehearsal()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "tools/Set-FieldStationSerials.ps1");
        var specPath = Path.Combine(repoRoot, "spec/Field_Udp_Smoke_Test.md");
        Assert.True(File.Exists(scriptPath), "Expected station serial pinning script does not exist.");
        Assert.True(File.Exists(specPath), "Expected field smoke spec does not exist.");

        var scriptText = File.ReadAllText(scriptPath);
        var specText = File.ReadAllText(specPath);

        Assert.Contains("DryRunDirectory", scriptText);
        Assert.Contains("AllowUnconnected is enabled", scriptText);
        Assert.Contains("-DryRunDirectory is only valid with -DryRun.", scriptText);
        Assert.Contains("-DryRunDirectory artifacts\\field-serial-pin-dry-run", specText);
        Assert.Contains("real config was not modified", specText);
        Assert.Contains("valid only with", specText);
    }

    [Fact]
    public void FieldReplaySmoke_RequiresConfidenceAndMotionEvidenceForEveryAppDrivingJoint()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "tools/Run-FieldReplaySmoke.ps1");
        Assert.True(File.Exists(scriptPath), "Expected field replay smoke script does not exist.");

        var scriptText = File.ReadAllText(scriptPath);

        Assert.Contains("required joint confidence evidence", scriptText);
        Assert.Contains("required joint motion evidence", scriptText);
        Assert.Contains("requiredJointConf=", scriptText);
        Assert.Contains("jointMotion=", scriptText);

        foreach (var jointName in FieldSkeletonAcceptancePolicy.AppDrivingJointNames)
        {
            Assert.Contains(jointName, scriptText);
        }
    }

    [Fact]
    public void FieldAcceptanceWrapper_RequiresAppFedLiveMotionDrillBeforeSuccess()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "tools/Run-FieldAcceptance.ps1");
        Assert.True(File.Exists(scriptPath), "Expected field acceptance script does not exist.");

        var scriptText = File.ReadAllText(scriptPath);

        Assert.Contains("Live app-fed motion drill", scriptText);
        Assert.Contains("\"-Mode\", \"Live\"", scriptText);
        Assert.Contains("\"-ForwardToApp\"", scriptText);
        Assert.Contains("\"-RequireMotionDrill\"", scriptText);
        Assert.Contains("\"-MotionThresholdMeters\"", scriptText);
        Assert.Contains("[pass] field acceptance completed", scriptText);

        Assert.True(
            scriptText.IndexOf("Live app-fed motion drill", StringComparison.Ordinal) <
            scriptText.IndexOf("[pass] field acceptance completed", StringComparison.Ordinal),
            "Field acceptance must not report success before the live app-fed motion drill step.");
    }

    [Fact]
    public void FieldAcceptanceWrapper_RestoresDirectRoutingAfterTeeAcceptance()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "tools/Run-FieldAcceptance.ps1");
        Assert.True(File.Exists(scriptPath), "Expected field acceptance script does not exist.");

        var scriptText = File.ReadAllText(scriptPath);

        Assert.Contains("RestoreDirectOnFailure", scriptText);
        Assert.Contains("KeepTeeAfterFailure", scriptText);
        Assert.Contains("Should-RestoreDirectAfterFailure", scriptText);
        Assert.Contains("Restore-DirectRoutingAfterFailure", scriptText);
        Assert.Contains("KeepTeeAfterSuccess", scriptText);
        Assert.Contains("Restore direct DSCC-to-Tauri routing after success", scriptText);
        Assert.Contains("\"-Mode\", \"Direct\"", scriptText);
        Assert.Contains("-RestoreDirectOnFailure and -KeepTeeAfterFailure cannot be used together.", scriptText);
    }

    [Fact]
    public void ReadinessReport_ChecksFieldAcceptanceWrapperContract()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "tools/Get-FieldReadinessReport.ps1");
        Assert.True(File.Exists(scriptPath), "Expected field readiness script does not exist.");

        var scriptText = File.ReadAllText(scriptPath);

        Assert.Contains("Add-FieldAcceptanceWrapperFailures", scriptText);
        Assert.Contains("Field handoff wrapper", scriptText);
        Assert.Contains("Run-FieldAcceptance final handoff requires app-fed live motion drill", scriptText);
        Assert.Contains("Live app-fed motion drill", scriptText);
        Assert.Contains("Restore direct DSCC-to-Tauri routing after success", scriptText);
        Assert.Contains("KeepTeeAfterFailure", scriptText);
    }

    [Fact]
    public void WpfLiveStartup_ValidatesStationPinsBeforeCreatingK4aTrackers()
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "src/DSCC.App.Wpf/ViewModels/MainWindowViewModel.cs");
        Assert.True(File.Exists(path), "Expected WPF main view model source does not exist.");

        var text = File.ReadAllText(path);

        Assert.Contains("OrbbecLiveStationPinPolicy.ValidateRequiredPins", text);
        Assert.Contains("ValidateLiveStationPins()", text);
        Assert.Contains("_config.AutoAssignDevicesOnStart", text);
        Assert.Contains("K4aBodyTrackingSkeletonSourceFactory.Create", text);
        Assert.Contains("CameraSerial = runtime.Station.AssignedCameraSerial", text);

        Assert.True(
            text.IndexOf("ValidateLiveStationPins()", StringComparison.Ordinal) <
            text.IndexOf("K4aBodyTrackingSkeletonSourceFactory.Create", StringComparison.Ordinal),
            "Live startup must validate pinned station serials before creating K4A body trackers.");
    }

    [Fact]
    public void ReadinessReport_ChecksDsccLiveStartupPinningContract()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "tools/Get-FieldReadinessReport.ps1");
        Assert.True(File.Exists(scriptPath), "Expected field readiness script does not exist.");

        var scriptText = File.ReadAllText(scriptPath);

        Assert.Contains("Add-DsccLiveStartupPinningFailures", scriptText);
        Assert.Contains("DSCC live startup contract", scriptText);
        Assert.Contains("DSCC WPF live startup validates pinned station serials", scriptText);
        Assert.Contains("pin validation before K4A tracker creation", scriptText);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DSCC.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate DSCC repository root from test output directory.");
    }
}
