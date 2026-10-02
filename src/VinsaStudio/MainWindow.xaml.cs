using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly PressureHistory _pressure = new();
    private readonly System.Diagnostics.Stopwatch _pressureClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly System.Windows.Threading.DispatcherTimer _pressureTimer = new() { Interval=TimeSpan.FromMilliseconds(50) };
    private StylusDevice? _activePen;
    private bool _penContact;
    private double PressureTime => _pressureClock.Elapsed.TotalSeconds;
    private bool _updating, _dirty, _refreshing;
    private DesktopSnapshot? _desktop;
    private bool _capturing;
    private int _tabletPresetIndex;
    private PenProfile _penProfile=PenProfile.Defaults;
    private PenBindingsWindow? _penEditor;
    private readonly string _penProfilePath=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VinsaStudio","pen-profile.json");
    private bool _started;
    private readonly MappingWindow _mapping;
    private readonly KeysWindow _keys;
    private MappingSurface ScreenSurface => _mapping.ScreenSurface;
    private MappingSurface TabletSurface => _mapping.TabletSurface;
    private ComboBox MonitorChoice => _mapping.MonitorChoice;
    private Button CaptureButton => _mapping.CaptureButton;
    private Button ApplyButton => _mapping.ApplyButton;
    private StackPanel ScreenFields => _mapping.ScreenFields;
    private StackPanel TabletFields => _mapping.TabletFields;
    private TextBlock DeviceDetails => _mapping.DeviceDetails;

    public MainWindow()
    {
        InitializeComponent();
        NativeWindowTheme.Attach(this);
        try { if(File.Exists(_penProfilePath)) _penProfile=PenProfile.Load(_penProfilePath); }
        catch(Exception ex) { SetStatus("Pen profile could not be loaded: "+ex.Message); }
        RefreshPenBindings();
        TestInk.PreviewStylusButtonDown += (_,e)=>ObservePenButton(e,true);
        TestInk.PreviewStylusButtonUp += (_,e)=>ObservePenButton(e,false);
        TestInk.PreviewKeyDown += (_,e)=> {
            var key=e.Key==Key.System?e.SystemKey:e.Key;
            _penEditor?.Observe("Keyboard event (device unverified)",$"{Keyboard.Modifiers} + {key}",true);
        };
        TestInk.PreviewMouseDown += (_,e)=> {
            if(e.ChangedButton==MouseButton.Left) return;
            _penEditor?.Observe(e.StylusDevice is null?"Mouse event (device unverified)":"Windows pen mouse event",e.ChangedButton.ToString(),true);
        };
        PenImage.Source=PenArtwork.Load();
        _pressureTimer.Tick += (_,_) => {
            if(_penContact && (_activePen is null || _activePen.InAir || !_activePen.InRange || !TestInk.IsStylusOver)) ReleasePressure();
            _pressure.Advance(PressureTime);
            if(TimelineCheck.IsChecked==true) RedrawPressure();
        };
        _pressureTimer.Start();
        Deactivated += (_,_) => HidePenCursor();
        _mapping = new MappingWindow();
        _keys = new KeysWindow();
        MonitorChoice.SelectionChanged += MonitorChanged;
        CaptureButton.Click += CaptureClick;
        _mapping.PresetButton.Click += CenterClick;
        _mapping.FullTabletButton.Click += FullTabletClick;
        _mapping.LoadButton.Click += LoadClick;
        _mapping.SaveButton.Click += SaveClick;
        ApplyButton.Click += ApplyClick;
        _mapping.NextDisplayButton.Click += (_,_) => {
            if(MonitorChoice.Items.Count>1) MonitorChoice.SelectedIndex=MonitorChoice.SelectedIndex<1 || MonitorChoice.SelectedIndex+1>=MonitorChoice.Items.Count?1:MonitorChoice.SelectedIndex+1;
        };
        CreateFields(ScreenFields, _screenFields);
        CreateFields(TabletFields, _tabletFields);
        TestInk.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Color.FromRgb(232,232,232), Width = 3, Height = 3,
            FitToCurve = true, IgnorePressure = false
        };
        ScreenSurface.AreaChanged += value => SelectionChanged(false, value);
        TabletSurface.AreaChanged += value => SelectionChanged(true, value);
        Populate();
        Loaded += async (_, _) =>
        {
            if (_started) return;
            _started=true;
            _mapping.Owner=this;_keys.Owner=this;
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
                var cursorProbe=new PenCursorOverlay();
                var tiltedDescription=new StylusPointDescription(new[] {
                    new StylusPointPropertyInfo(StylusPointProperties.X),
                    new StylusPointPropertyInfo(StylusPointProperties.Y),
                    new StylusPointPropertyInfo(StylusPointProperties.NormalPressure),
                    new StylusPointPropertyInfo(StylusPointProperties.XTiltOrientation,-9000,9000,StylusPointPropertyUnit.Degrees,100),
                    new StylusPointPropertyInfo(StylusPointProperties.YTiltOrientation,-9000,9000,StylusPointPropertyUnit.Degrees,100)
                });
                if(!cursorProbe.Update(new StylusPoint(10,10,0.5f,tiltedDescription,new[]{4500,-3000})))
                    throw new InvalidOperationException("Supported tilt properties were not recognized.");
                if(cursorProbe.Update(new StylusPoint(10,10)))
                    throw new InvalidOperationException("Missing tilt properties must use the tip-only fallback.");
                await RenderPreview(this,"MainWindow");
                var profileEditor=new PenBindingsWindow(_penProfile,_penProfilePath,true) {Owner=this};
                profileEditor.Show();await RenderPreview(profileEditor,"PenCustomization");profileEditor.Close();
                TimelineCheck.IsChecked=true;_pressure.Advance(PressureTime);RedrawPressure();
                await RenderPreview(this,"ContinuousTimeline");TimelineCheck.IsChecked=false;
                PressureTuningSlider.Value=-255;
                if(PressureTuningSlider.Value!=-255) throw new InvalidOperationException("Slider minimum is incorrect.");
                PressureTuningSlider.Value=255;
                if(PressureTuningSlider.Value!=255) throw new InvalidOperationException("Slider maximum is incorrect.");
                ResetPressureClick(this,new RoutedEventArgs());
                if(PressureTuningSlider.Value!=0) throw new InvalidOperationException("Slider reset failed.");
                _mapping.Show(); await RenderPreview(_mapping,"ScreenMapping");
                AreaRect previousArea=_settings.TabletArea;
                _mapping.PresetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(_settings.TabletArea==previousArea) throw new InvalidOperationException("Preset button did not update selection.");
                _mapping.Close(); _mapping.Show();
                if(TabletSurface.Area!=_settings.TabletArea) throw new InvalidOperationException("Mapping selection was lost when reopening.");
                _settings.TabletArea=previousArea;_dirty=false;Populate();_mapping.Hide();
                _keys.Show();
                ((Button)_keys.KeyButtons.Children[9]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(_keys.ButtonNumber.Text!="10" || _keys.KeySummary.Text!="Alt") throw new InvalidOperationException("Key selection did not update.");
                ((Button)_keys.KeyButtons.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await RenderPreview(_keys,"DefinedKeys"); _keys.Hide();
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
            var group = new StackPanel { Width = 110, Margin = new Thickness(0, 0, 10, 0) };
            group.Children.Add(new TextBlock { Text = labels[i], Foreground = (Brush)FindResource("MutedText"), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
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
        bool hideMapping = capture && _mapping.IsVisible;
        try
        {
            if (hidden) Hide();
            if (hideMapping) _mapping.Hide();
            if (hidden || hideMapping) await Task.Delay(160);
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
            if (hidden) Show();
            if (hideMapping) { _mapping.Show(); _mapping.Activate(); }
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
    private void GuardClosing(object? sender, CancelEventArgs e)
    {
        if(!MayDiscard()) { e.Cancel=true;return; }
        _pressureTimer.Stop();
        _penEditor?.Close();
        _mapping.AllowClose=true;_keys.AllowClose=true;
        _mapping.Close();_keys.Close();
    }

    private async Task RefreshDevices()
    {
        if (_refreshing) return;
        _refreshing = true; ApplyButton.IsEnabled = false;
        try
        {
            _devices = await Task.Run(() => WindowsHid.Enumerate());
            HidTablet[] eligible = _devices.Where(WindowsHid.IsEligible).ToArray();
            ConnectionBadge.Text = eligible.Length == 1 ? "Tablet connected" : _devices.Count > 0 ? "Tablet detected" : "Tablet disconnected";
            ConnectionPanel.Background = new SolidColorBrush(eligible.Length==1 ? Color.FromRgb(41,41,41) : Color.FromRgb(33,33,33));
            ConnectionBadge.Foreground = new SolidColorBrush(eligible.Length==1 ? Color.FromRgb(214,214,214) : Color.FromRgb(166,166,166));
            DeviceDetails.Text = _devices.Count == 0 ? "No supported T906 HID collection found.\nConnect your tablet and refresh." : string.Join("\n\n", _devices.Select(d => $"{d.Product}\nUSB {d.VendorId:X4}:{d.ProductId:X4}  ·  Version {d.Version:X4}\nFeature report length: {d.FeatureLength}\n{d.Path}"));
            ApplyButton.IsEnabled = eligible.Length == 1;
        }
        catch (Exception ex) { ConnectionBadge.Text = "Device check failed"; DeviceDetails.Text = ex.Message; }
        finally { _refreshing = false; }
    }

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
        UpdatePenCursor(e);
        if (!e.StylusDevice.TabletDevice.TabletHardwareCapabilities.HasFlag(TabletHardwareCapabilities.SupportsPressure))
        {
            PressureValue.ToolTip = "Windows is not exposing pressure for this pen";
            return;
        }
        foreach (StylusPoint point in e.GetStylusPoints(TestInk))
        {
            double value = Math.Clamp(point.PressureFactor, 0, 1);
            _activePen=e.StylusDevice;_penContact=true;
            _pressure.Record(value,PressureTime);
            PressureValue.Text = ((int)Math.Round(value*8192)).ToString(); PressureGauge.Value=value*8192;
        }
        RedrawPressure();
    }
    private void PenUp(object sender, StylusEventArgs e)
    {
        if (e.StylusDevice?.TabletDevice.Type != TabletDeviceType.Stylus
            || !e.StylusDevice.TabletDevice.TabletHardwareCapabilities.HasFlag(TabletHardwareCapabilities.SupportsPressure)) return;
        ReleasePressure();
    }
    private void ReleasePressure()
    {
        if(_penContact) _pressure.Record(0,PressureTime);
        _penContact=false;PressureValue.Text="0";PressureGauge.Value=0;RedrawPressure();
    }
    private void UpdatePenCursor(StylusEventArgs e)
    {
        var points=e.GetStylusPoints(TestInk);
        if(points.Count==0) return;
        bool tilt=PenCursor.Update(points[^1]);
        TestInk.ToolTip=tilt ? "Windows pen tilt" : "Pen tilt is not exposed by Windows; showing tip position only.";
        TestInk.UseCustomCursor=true;TestInk.Cursor=Cursors.None;
    }
    private void PenHover(object sender,StylusEventArgs e)
    {
        if(e.StylusDevice?.TabletDevice.Type!=TabletDeviceType.Stylus) return;
        UpdatePenCursor(e);ReleasePressure();
    }
    private void PenLeave(object sender,StylusEventArgs e) => HidePenCursor();
    private void HidePenCursor()
    {
        PenCursor.Visibility=Visibility.Collapsed;TestInk.UseCustomCursor=false;TestInk.ClearValue(CursorProperty);ReleasePressure();
    }
    private void PressureModeChanged(object sender,RoutedEventArgs e) { if(PressureChart is not null) RedrawPressure(); }
    private void ResetPressureClick(object sender,RoutedEventArgs e) => PressureTuningSlider.Value=0;
    private void RedrawPressure()
    {
        double width = PressureChart.ActualWidth, height = PressureChart.ActualHeight;
        var values = TimelineCheck.IsChecked==true ? _pressure.Timeline : _pressure.Events;
        var points = new PointCollection();
        for (int i = 0; i < values.Count; i++) points.Add(new Point(i / 199.0 * width, height - values[i] * (height - 8) - 4));
        if(values.Count==0) { points.Add(new Point(0,height-2));points.Add(new Point(width,height-2)); }
        PressureLine.Points = points;
    }
    private void ClearClick(object sender, RoutedEventArgs e) { TestInk.Strokes.Clear(); _pressure.Clear(PressureTime); _penContact=false; RedrawPressure(); PressureValue.Text = "0"; PressureGauge.Value=0; }

    private async Task RenderPreview(Window window,string name)
    {
        window.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        if(App.PreviewDirectory is null) return;
        Directory.CreateDirectory(App.PreviewDirectory);
        var visual=(FrameworkElement)window.Content;
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output=new FileStream(System.IO.Path.Combine(App.PreviewDirectory,name+".png"),FileMode.CreateNew);
        encoder.Save(output);
    }
    private void MappingClick(object sender,RoutedEventArgs e) { if(_mapping.WindowState==WindowState.Minimized) _mapping.WindowState=WindowState.Normal; _mapping.Show(); _mapping.Activate(); }
    private void RefreshPenBindings()
    {
        UpperBindingButton.Content="Upper: "+_penProfile.Upper.Label;
        LowerBindingButton.Content="Lower: "+_penProfile.Lower.Label;
        UpperBindingButton.ToolTip=_penProfile.Upper.Label+" · draft profile";
        LowerBindingButton.ToolTip=_penProfile.Lower.Label+" · draft profile";
    }
    private void OpenPenBindings(bool upper)
    {
        if(_penEditor is null)
        {
            _penEditor=new PenBindingsWindow(_penProfile,_penProfilePath,upper) {Owner=this};
            _penEditor.ProfileSaved += profile=> { _penProfile=profile;RefreshPenBindings(); };
            _penEditor.Closed += (_,_)=>_penEditor=null;
        }
        else _penEditor.SelectButton(upper);
        if(_penEditor.WindowState==WindowState.Minimized) _penEditor.WindowState=WindowState.Normal;
        _penEditor.Show();_penEditor.Activate();
    }
    private void PenBindingsClick(object sender,RoutedEventArgs e)=>OpenPenBindings(true);
    private void UpperBindingClick(object sender,RoutedEventArgs e)=>OpenPenBindings(true);
    private void LowerBindingClick(object sender,RoutedEventArgs e)=>OpenPenBindings(false);
    private void ObservePenButton(StylusButtonEventArgs e,bool pressed)
    {
        _penEditor?.Observe(e.StylusButton.Name,e.StylusButton.Guid.ToString(),pressed);
        SetStatus($"Pen button: {e.StylusButton.Name} {(pressed?"pressed":"released")}. Vendor assignment remains active.");
    }
    private void ButtonsClick(object sender,RoutedEventArgs e) { if(_keys.WindowState==WindowState.Minimized) _keys.WindowState=WindowState.Normal; _keys.Show(); _keys.Activate(); }
    private async void RefreshConnection(object sender,MouseButtonEventArgs e) => await RefreshDevices();
    private void WebsiteClick(object sender,RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.ping-it.cn") { UseShellExecute=true }); }
        catch(Exception ex) { MessageBox.Show(this,ex.Message,"Website could not be opened",MessageBoxButton.OK,MessageBoxImage.Information); }
    }
    private void RestoreClick(object sender,RoutedEventArgs e)
    {
        var defaults=TabletSettings.Defaults();
        _settings.ScreenArea=defaults.ScreenArea;_settings.TabletArea=defaults.TabletArea;
        _settings.InkMode=defaults.InkMode;_settings.LeftHandMode=defaults.LeftHandMode;
        _dirty=true;Populate();SetStatus("Mapping and mode defaults restored locally. Save or apply from Screen Mapping.");
    }
    private void SetStatus(string message) { StatusText.Text=message;ConnectionPanel.ToolTip=message; }
}
