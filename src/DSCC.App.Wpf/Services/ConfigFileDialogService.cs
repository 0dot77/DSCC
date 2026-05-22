using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace DSCC.App.Wpf.Services;

public interface IConfigFileDialogService
{
    bool TryPickOpenConfig(string? currentConfigPath, out string selectedPath);

    bool TryPickSaveConfig(string? currentConfigPath, out string selectedPath);
}

public sealed class ConfigFileDialogService : IConfigFileDialogService
{
    private const string ConfigFilter = "DSCC config (*.json)|*.json|All files (*.*)|*.*";
    private const string DefaultExtension = ".json";

    public bool TryPickOpenConfig(string? currentConfigPath, out string selectedPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open DSCC config",
            Filter = ConfigFilter,
            DefaultExt = DefaultExtension,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        ApplyInitialPath(dialog, currentConfigPath);

        if (dialog.ShowDialog(GetOwner()) == true)
        {
            selectedPath = Path.GetFullPath(dialog.FileName);
            return true;
        }

        selectedPath = string.Empty;
        return false;
    }

    public bool TryPickSaveConfig(string? currentConfigPath, out string selectedPath)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save DSCC config as",
            Filter = ConfigFilter,
            DefaultExt = DefaultExtension,
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = true
        };

        ApplyInitialPath(dialog, currentConfigPath);

        if (dialog.ShowDialog(GetOwner()) == true)
        {
            selectedPath = Path.GetFullPath(dialog.FileName);
            return true;
        }

        selectedPath = string.Empty;
        return false;
    }

    private static void ApplyInitialPath(FileDialog dialog, string? currentConfigPath)
    {
        if (string.IsNullOrWhiteSpace(currentConfigPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(currentConfigPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }

        var fileName = Path.GetFileName(fullPath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            dialog.FileName = fileName;
        }
    }

    private static Window? GetOwner()
    {
        return Application.Current?.MainWindow;
    }
}
