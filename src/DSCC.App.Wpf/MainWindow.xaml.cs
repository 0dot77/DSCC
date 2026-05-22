using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DSCC.App.Wpf.ViewModels;

namespace DSCC.App.Wpf;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private StationRowViewModel? _dragStation;
    private Canvas? _dragCanvas;
    private StageDragTarget _dragTarget = StageDragTarget.None;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void EditorFootMarker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginStageDrag(sender, e, StageDragTarget.FootMarker);
    }

    private void EditorRoi_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginStageDrag(sender, e, StageDragTarget.Roi);
    }

    private void StageEditorCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStation is null || _dragCanvas is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        UpdateStageDrag(e);
    }

    private void StageEditorCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStation is not null)
        {
            UpdateStageDrag(e);
            _viewModel.ApplyStationEditorCommand.Execute(_dragStation.StationId);
        }

        EndStageDrag();
    }

    private void StageEditorCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        EndStageDrag();
    }

    private void BeginStageDrag(object sender, MouseButtonEventArgs e, StageDragTarget target)
    {
        if (sender is not FrameworkElement { DataContext: StationRowViewModel station } element)
        {
            return;
        }

        _dragStation = station;
        _dragTarget = target;
        _dragCanvas = FindAncestor<Canvas>(element);
        _dragCanvas?.CaptureMouse();
        UpdateStageDrag(e);
        e.Handled = true;
    }

    private void UpdateStageDrag(MouseEventArgs e)
    {
        if (_dragStation is null || _dragCanvas is null)
        {
            return;
        }

        var point = e.GetPosition(_dragCanvas);
        switch (_dragTarget)
        {
            case StageDragTarget.FootMarker:
                _dragStation.MoveFootMarkerFromCanvas(point.X, point.Y);
                break;
            case StageDragTarget.Roi:
                _dragStation.MoveRoiCenterFromCanvas(point.X, point.Y);
                break;
        }
    }

    private void EndStageDrag()
    {
        _dragCanvas?.ReleaseMouseCapture();
        _dragStation = null;
        _dragCanvas = null;
        _dragTarget = StageDragTarget.None;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private enum StageDragTarget
    {
        None,
        FootMarker,
        Roi
    }
}
