using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using VinsaStudio.Core;
using VinsaStudio.Controls;

namespace VinsaStudio;

public partial class MainWindow : Window
{
    private TabletSettings _settings = TabletSettings.Defaults();
    private readonly TextBox[] _screenFields = new TextBox[4];
    private readonly TextBox[] _tabletFields = new TextBox[4];
    private IReadOnlyList<HidTablet> _devices = [];
    private readonly Queue<double> _pressure = new();
    private bool _updating, _dirty, _refreshing;
    private DesktopSnapshot? _desktop;
    private bool _capturing;
    private int _tabletPresetIndex;

    public MainWindow()
    {
        InitializeComponent();
        for (int number = 6; number <= 10; number++)
            ShortcutTiles.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(41, 53, 62)), Margin = new Thickness(4), Padding = new Thickness(12),
                Child = new TextBlock { Text = number.ToString(), HorizontalAlignment = HorizontalAlignment.Center }
            });
        CreateFields(ScreenFields, _screenFields);
        CreateFields(TabletFields, _tabletFields);
        TestInk.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Color.FromRgb(25, 63, 55), Width = 3, Height = 3,
            FitToCurve = true, IgnorePressure = false
        };
        ScreenSurface.AreaChanged += value => SelectionChanged(false, value);
        TabletSurface.AreaChanged += value => SelectionChanged(true, value);
        Populate();
        Loaded += async (_, _) =>
        {
            string vendorPath = App.InitialSettingsPath ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MyTablet", "TSetting.ini");
            if (File.Exists(vendorPath))
            {
                try { _settings = TabletSettings.Load(vendorPath); Populate(); SetStatus("Loaded vendor settings · Original file stays intact"); }
                catch (Exception ex) { SetStatus("Could not load vendor settings: " + ex.Message); }
            }
            await RefreshDevices();
            await RefreshDesktop(!App.SmokeTest || App.PreviewDirectory is not null);
            if (App.SmokeTest)
            {
                foreach (StackPanel page in new[] { MappingPage, ButtonsPage, PressurePage, DevicePage })
                {
                    ShowPage(page, page == MappingPage ? "Your drawing space" : "Render check", "Read-only smoke test"); UpdateLayout();
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                    if (App.PreviewDirectory is not null)
                    {
                        Directory.CreateDirectory(App.PreviewDirectory);
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);
                        bitmap.Render(this);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var output = new FileStream(System.IO.Path.Combine(App.PreviewDirectory,page.Name+".png"),FileMode.CreateNew);
                        encoder.Save(output);
                    }
                }
                Close();
            }
        };
        Closing += GuardClosing;
    }

    private void CreateFields(StackPanel host, TextBox[] fields)
    {
        string[] labels = ["Left", "Top", "Right", "Bottom"];
        for (int i = 0; i < fields.Length; i++)
        {
            var group = new StackPanel { Width = 120, Margin = new Thickness(0, 0, 14, 0) };
            group.Children.Add(new TextBlock { Text = labels[i], Foreground = new SolidColorBrush(Color.FromRgb(145, 160, 172)), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
            var input = new TextBox(); fields[i] = input;
            input.TextChanged += FieldChanged;
            group.Children.Add(input); host.Children.Add(group);
        }
    }

    private void Populate()
    {
        _updating = true;
        SetFields(_screenFields, _settings.ScreenArea); SetFields(_tabletFields, _settings.TabletArea);
        InkCheck.IsChecked = _settings.InkMode; LeftCheck.IsChecked = _settings.LeftHandMode;
        _updating = false;
        DrawSelections();
    }

    private static void SetFields(TextBox[] fields, AreaRect area)
    {
        int[] values = [area.Left, area.Top, area.Right, area.Bottom];
        for (int i = 0; i < 4; i++) fields[i].Text = values[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static AreaRect ReadFields(TextBox[] fields)
    {
        int[] values = fields.Select(f => int.TryParse(f.Text, out int value) ? value : throw new ArgumentException("Enter whole coordinates between 0 and 4095.")).ToArray();
        return new AreaRect(values[0], values[1], values[2], values[3]);
    }

    private bool ValidateFields(bool showError = false)
    {
        try
        {
            AreaRect screen = ReadFields(_screenFields), tablet = ReadFields(_tabletFields);
            _settings.ScreenArea = screen; _settings.TabletArea = tablet;
            _settings.InkMode = InkCheck.IsChecked == true; _settings.LeftHandMode = LeftCheck.IsChecked == true;
            _settings.ToBytes(); DrawSelections(); return true;
        }
        catch (Exception ex)
        {
            SetStatus("Check mapping: " + ex.Message);
            if (showError) MessageBox.Show(this, ex.Message, "Check mapping", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
    }

    private void FieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        _dirty = true;
        if (ValidateFields()) SetStatus("Unsaved local changes · Device unchanged");
    }

    private void FlagChanged(object sender, RoutedEventArgs e)
    {
        if (_updating || _screenFields[0] is null) return;
        _dirty = true;
        if (ValidateFields()) SetStatus("Unsaved local changes · Device unchanged");
    }

    private void DrawSelections()
    {
        if (ScreenSurface is null) return;
        ScreenSurface.Area = _settings.ScreenArea;
        TabletSurface.Area = _settings.TabletArea;
        SyncMonitorChoice();
    }

    private void SyncMonitorChoice()
    {
        if (_desktop is null || MonitorChoice.Items.Count == 0) return;
        bool updating = _updating;
        _updating = true;
        int selected = _settings.ScreenArea == AreaRect.Full ? 1 : 0;
        for (int i = 0; i < _desktop.Monitors.Count && selected == 0; i++)
        {
            var m = _desktop.Monitors[i];
            if (_settings.ScreenArea == MappingGeometry.MonitorArea(m.X,m.Y,m.Width,m.Height,
                _desktop.X,_desktop.Y,_desktop.Width,_desktop.Height)) selected = i + 2;
        }
        MonitorChoice.SelectedIndex = selected;
        _updating = updating;
    }

    private void SelectionChanged(bool tablet, AreaRect value)
    {
        if (tablet) _settings.TabletArea = value; else _settings.ScreenArea = value;
        _dirty = true;
        Populate();
        SetStatus("Unsaved selection · Device unchanged");
    }

    private async Task RefreshDesktop(bool capture)
    {
        if (_capturing) return;
        _capturing = true;
        CaptureButton.IsEnabled = false;
        bool hidden = capture && IsVisible;
        try
        {
            if (hidden) { Hide(); await Task.Delay(160); }
            _desktop = await Task.Run(() => DesktopCapture.Read(capture));
            ScreenSurface.Desktop = _desktop;
            _updating = true;
            MonitorChoice.Items.Clear();
            MonitorChoice.Items.Add("Custom area");
            MonitorChoice.Items.Add("All displays");
            for (int i=0;i<_desktop.Monitors.Count;i++)
            {
                var monitor=_desktop.Monitors[i];
                MonitorChoice.Items.Add($"Display {i+1}"+(monitor.Primary?" · main":""));
            }
            SyncMonitorChoice();
        }
        catch (Exception ex) { SetStatus("Desktop preview unavailable: " + ex.Message); }
        finally
        {
            _updating = false;
            if (hidden) { Show(); Activate(); }
            CaptureButton.IsEnabled = true;
            _capturing = false;
        }
    }

    private async void CaptureClick(object sender, RoutedEventArgs e) => await RefreshDesktop(true);
    private void MonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _desktop is null || MonitorChoice.SelectedIndex <= 0) return;
        var index=MonitorChoice.SelectedIndex-2;
        AreaRect value=AreaRect.Full;
        if (index>=0 && index<_desktop.Monitors.Count)
        {
            var monitor=_desktop.Monitors[index];
            value=MappingGeometry.MonitorArea(monitor.X,monitor.Y,monitor.Width,monitor.Height,
                _desktop.X,_desktop.Y,_desktop.Width,_desktop.Height);
        }
        SelectionChanged(false,value);
    }

    private void FullTabletClick(object sender, RoutedEventArgs e) => SelectionChanged(true,AreaRect.Full);

    private void FullClick(object sender, RoutedEventArgs e)
    {
        _settings.ScreenArea = AreaRect.Full; _settings.TabletArea = AreaRect.Full;
        _dirty = true; Populate(); SetStatus("Full area selected · Unsaved local changes");
    }
    private void CenterClick(object sender, RoutedEventArgs e)
    {
        AreaRect value = MappingGeometry.TabletPreset(_tabletPresetIndex);
        if (value == _settings.TabletArea)
        {
            _tabletPresetIndex = (_tabletPresetIndex + 1) % 4;
            value = MappingGeometry.TabletPreset(_tabletPresetIndex);
        }
        string[] labels = ["Full tablet", "Inset area", "Centered area", "Vendor 16:9 area"];
        string label = labels[_tabletPresetIndex];
        _tabletPresetIndex = (_tabletPresetIndex + 1) % 4;
        SelectionChanged(true,value);
        SetStatus(label + " selected · Unsaved local changes");
    }

    private void LoadClick(object sender, RoutedEventArgs e)
    {
        if (!MayDiscard()) return;
        var dialog = new OpenFileDialog { Title = "Load tablet settings", Filter = "Tablet settings (*.ini)|*.ini|All files (*.*)|*.*", FileName = "TSetting.ini" };
        if (dialog.ShowDialog(this) != true) return;
        try { _settings = TabletSettings.Load(dialog.FileName); _dirty = false; Populate(); SetStatus("Loaded " + dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Settings could not be loaded", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private bool SaveSettings()
    {
        if (!ValidateFields(true)) return false;
        var dialog = new SaveFileDialog { Title = "Save a new settings copy", Filter = "Tablet settings (*.ini)|*.ini", FileName = "VinsaStudio-settings.ini", DefaultExt = ".ini", AddExtension = true, OverwritePrompt = false };
        if (dialog.ShowDialog(this) != true) return false;
        try
        {
            if (_settings.SourcePath is not null && string.Equals(System.IO.Path.GetFullPath(dialog.FileName), System.IO.Path.GetFullPath(_settings.SourcePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a different filename to preserve the original settings.");
            if (File.Exists(dialog.FileName)) throw new InvalidOperationException("A file already exists there. Choose a new filename for this settings copy.");
            _settings.SaveCopy(dialog.FileName); _dirty = false; SetStatus("Saved copy to " + dialog.FileName); return true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Settings could not be saved", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }
    private void SaveClick(object sender, RoutedEventArgs e) => SaveSettings();

    private bool MayDiscard()
    {
        if (!_dirty) return true;
        var answer = MessageBox.Show(this, "Save your local settings changes before continuing?", "Unsaved settings", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && SaveSettings());
    }
    private void GuardClosing(object? sender, CancelEventArgs e) { if (!MayDiscard()) e.Cancel = true; }

    private async Task RefreshDevices()
    {
        if (_refreshing) return;
        _refreshing = true; ApplyButton.IsEnabled = false;
        try
        {
            _devices = await Task.Run(() => WindowsHid.Enumerate());
            HidTablet[] eligible = _devices.Where(WindowsHid.IsEligible).ToArray();
            ConnectionBadge.Text = eligible.Length == 1 ? "T906 connected" : _devices.Count > 0 ? "Tablet detected" : "Tablet disconnected";
            DeviceDetails.Text = _devices.Count == 0 ? "No supported T906 HID collection found.\nConnect your tablet and refresh." : string.Join("\n\n", _devices.Select(d => $"{d.Product}\nUSB {d.VendorId:X4}:{d.ProductId:X4}  ·  Version {d.Version:X4}\nFeature report length: {d.FeatureLength}\n{d.Path}"));
            ApplyButton.IsEnabled = eligible.Length == 1;
        }
        catch (Exception ex) { ConnectionBadge.Text = "Device check failed"; DeviceDetails.Text = ex.Message; }
        finally { _refreshing = false; }
    }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshDevices();

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (!ValidateFields(true)) return;
        HidTablet[] eligible = _devices.Where(WindowsHid.IsEligible).ToArray();
        if (eligible.Length != 1) { SetStatus("Connect exactly one eligible tablet, then refresh."); return; }
        string reports = string.Join("\n", FeatureProtocol.Plan(_settings, eligible[0].Version).Select(Convert.ToHexString));
        string message = "This sends the three reconstructed mapping reports to your tablet. Hardware behavior has not yet been validated against the vendor app.\n\nIt changes tablet area, mode/handedness, and screen area; it is not the complete vendor initialization. MyTablet may overwrite these settings while running.\n\n" + reports + "\n\nApply this mapping now?";
        if (MessageBox.Show(this, message, "Apply mapping to hardware", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsEnabled = false;
        ApplyButton.IsEnabled = false;
        try { await Task.Run(() => WindowsHid.ApplyMapping(eligible[0], _settings)); SetStatus("Mapping reports sent · Verify pen behavior before relying on this mapping"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message + "\n\nSome reports may already have been sent. Reopen MyTablet to restore its saved configuration if needed.", "Mapping apply failed", MessageBoxButton.OK, MessageBoxImage.Error); SetStatus("Mapping apply failed · Device state may be partially changed"); }
        finally { IsEnabled = true; await RefreshDevices(); }
    }

    private void PenInput(object sender, StylusEventArgs e)
    {
        if (e.StylusDevice is null || e.StylusDevice.TabletDevice.Type != TabletDeviceType.Stylus || e.StylusDevice.InAir) return;
        if (!e.StylusDevice.TabletDevice.TabletHardwareCapabilities.HasFlag(TabletHardwareCapabilities.SupportsPressure))
        {
            PressureValue.Text = "Windows is not exposing pressure for this pen";
            return;
        }
        foreach (StylusPoint point in e.GetStylusPoints(TestInk))
        {
            double value = Math.Clamp(point.PressureFactor, 0, 1); _pressure.Enqueue(value);
            while (_pressure.Count > 200) _pressure.Dequeue();
            PressureValue.Text = $"{value:P0} · Windows pen pressure";
        }
        RedrawPressure();
    }
    private void PenUp(object sender, StylusEventArgs e)
    {
        if (e.StylusDevice?.TabletDevice.Type != TabletDeviceType.Stylus
            || !e.StylusDevice.TabletDevice.TabletHardwareCapabilities.HasFlag(TabletHardwareCapabilities.SupportsPressure)) return;
        _pressure.Enqueue(0); while (_pressure.Count > 200) _pressure.Dequeue(); RedrawPressure();
        PressureValue.Text = "Pen lifted · 0%";
    }
    private void RedrawPressure()
    {
        double width = PressureChart.ActualWidth, height = PressureChart.ActualHeight;
        double[] values = _pressure.ToArray(); var points = new PointCollection();
        for (int i = 0; i < values.Length; i++) points.Add(new Point(i / 199.0 * width, height - values[i] * (height - 8) - 4));
        PressureLine.Points = points;
    }
    private void ClearClick(object sender, RoutedEventArgs e) { TestInk.Strokes.Clear(); _pressure.Clear(); PressureLine.Points.Clear(); PressureValue.Text = "Waiting for pen input"; }

    private void ShowPage(StackPanel page, string title, string subtitle)
    {
        foreach (StackPanel candidate in new[] { MappingPage, ButtonsPage, PressurePage, DevicePage }) candidate.Visibility = candidate == page ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = title; PageSubtitle.Text = subtitle;
    }
    private void MappingClick(object sender, RoutedEventArgs e) => ShowPage(MappingPage, "Your drawing space", "Choose where your pen moves, and where it lands.");
    private void ButtonsClick(object sender, RoutedEventArgs e) => ShowPage(ButtonsPage, "Make every button count", "Existing assignments work; reconstruction is still in progress.");
    private void PressureClick(object sender, RoutedEventArgs e) => ShowPage(PressurePage, "Find your natural stroke", "Test the pressure Windows receives from your pen.");
    private void DeviceClick(object sender, RoutedEventArgs e) => ShowPage(DevicePage, "Know your tablet", "Read-only connection details and verified configuration scope.");
    private void SetStatus(string message) => StatusText.Text = message;
}
