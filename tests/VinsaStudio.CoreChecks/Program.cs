using System.Text;
using System.Text.Json;
using VinsaStudio.Core;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
}
void Reject(Action action, string description)
{
    bool rejected = false;
    try { action(); } catch (ArgumentException) { rejected = true; } catch (IOException) { rejected = true; }
    Check(rejected, description);
}
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var vendor = Path.Combine(root, "downloads/windows/installed/MyTablet/TSetting.ini");
var settings = TabletSettings.Load(vendor);
Check(settings.ToBytes().SequenceEqual(File.ReadAllBytes(vendor)), "Vendor settings must round-trip byte-for-byte.");
Check(settings.TabletArea == new AreaRect(650, 650, 3500, 3500), "Saved tablet mapping.");
Check(settings.ScreenArea == new AreaRect(1753, 0, 4095, 4095), "Saved screen mapping.");
var reports = FeatureProtocol.Plan(settings, 0x2023);
Check(Convert.ToHexString(reports[0]) == "0807228A8ABB2222", "Saved tablet report packing.");
Check(Convert.ToHexString(reports[1]) == "0806010000000000", "Firmware version ink override.");
Check(Convert.ToHexString(reports[2]) == "0801D9269600FFF0", "Saved screen report packing.");
Check(FeatureProtocol.Plan(settings, 0x1802)[1][2] == 0, "Version boundary.");
Reject(() => new AreaRect(0, 0, 0, 4095), "Reject empty rectangle.");
Reject(() => new AreaRect(-1, 0, 4095, 4095), "Reject negative coordinate.");
Reject(() => settings.SaveCopy(vendor.ToUpperInvariant()), "Refuse original regardless of case.");

var temporary = Path.Combine(Path.GetTempPath(), "vinsa-core-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var file = Path.Combine(temporary, "settings.ini");
    var bytes = Encoding.Latin1.GetBytes("\u00ef\u00bb\u00bf;Unknown \u00ff bytes\r\r\n" + Encoding.ASCII.GetString(TabletSettings.Defaults().ToBytes())
        .Replace("Smoothness=0", "Smoothness = 0005  ") + "[Other]\nUnknown=anything\n");
    File.WriteAllBytes(file, bytes);
    var modified = TabletSettings.Load(file);
    Check(modified.ToBytes().SequenceEqual(bytes), "BOM, legacy bytes, mixed terminators, padded numbers preserved.");
    modified.LeftHandMode = true;
    var expected = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(bytes).Replace("LeftHandMode=0", "LeftHandMode=1"));
    Check(modified.ToBytes().SequenceEqual(expected), "Edit only the selected value.");
    Check(modified.Smoothness == 5, "Preserve unresolved smoothness value.");
    var copy = Path.Combine(temporary, "copy.ini");
    modified.SaveCopy(copy);
    Check(File.ReadAllBytes(copy).SequenceEqual(expected), "Save copy content.");
    Reject(() => modified.SaveCopy(copy), "Refuse overwriting another existing file.");
    modified.Smoothness = -1;
    Reject(() => modified.ToBytes(), "Reject negative smoothness.");
}
finally { Directory.Delete(temporary, true); }

var device = new HidTablet(@"\\?\hid#vid_08f2&pid_6811&mi_02&col01#test", "T906", 0x08f2, 0x6811, 0x2023, 8);
Check(WindowsHid.IsEligible(device), "Eligible tablet feature collection.");
Check(!WindowsHid.IsEligible(device with { FeatureLength = 0 }), "Reject non-feature collection.");
Check(!WindowsHid.IsEligible(device with { Path = device.Path.Replace("col01", "col010") }), "Require complete collection component.");
Check(!WindowsHid.IsEligible(device with { ProductId = 0x9999 }), "Reject unrelated tablet.");
Reject(() => WindowsHid.ValidateReport(device, new byte[] { 8, 5, 0, 0, 0, 0, 0, 0 }), "Reject unknown command.");
Reject(() => WindowsHid.ValidateReport(device, new byte[] { 8, 7 }), "Reject truncated report.");
foreach (var report in reports) WindowsHid.ValidateReport(device, report);

var region = new AreaRect(650,650,3500,3500);
Check(MappingGeometry.TabletPreset(3)==new AreaRect(0,148,4095,3948), "T906 vendor 16:9 coordinates recovered from model-specific branch.");
Check(MappingGeometry.Drag(region,AreaHandle.Move,-9999,9999) == new AreaRect(0,1245,2850,4095), "Moving to a boundary preserves selection size.");
Check(MappingGeometry.Drag(region,AreaHandle.Left|AreaHandle.Top,9999,9999) == new AreaRect(3499,3499,3500,3500), "Crossed corner remains a positive area.");
Check(MappingGeometry.Drag(region,AreaHandle.Right|AreaHandle.Bottom,9999,9999) == new AreaRect(650,650,4095,4095), "Expanded corner stops at tablet bounds.");
Check(MappingGeometry.MonitorArea(-1920,0,1920,1080,-1920,0,3840,1080) == new AreaRect(0,0,2048,4095), "Negative monitor origin maps to left half.");
Check(MappingGeometry.MonitorArea(0,-1080,1920,1080,0,-1080,1920,2160) == new AreaRect(0,0,4095,2048), "Vertically stacked monitor maps to top half.");
Reject(() => MappingGeometry.MonitorArea(0,0,0,1080,0,0,1920,1080), "Reject zero-sized monitor.");
var handles = new[] { AreaHandle.Move, AreaHandle.Left, AreaHandle.Right, AreaHandle.Top, AreaHandle.Bottom,
    AreaHandle.Left|AreaHandle.Top, AreaHandle.Left|AreaHandle.Bottom, AreaHandle.Right|AreaHandle.Top, AreaHandle.Right|AreaHandle.Bottom };
var dragRandom = new Random(906);
bool validDrags = true;
for (int i=0;i<1000;i++)
{
    int l=dragRandom.Next(4095), t=dragRandom.Next(4095);
    var original=new AreaRect(l,t,dragRandom.Next(l+1,4096),dragRandom.Next(t+1,4096));
    foreach(var handle in handles)
    {
        var moved=MappingGeometry.Drag(original,handle,dragRandom.Next(-8190,8191),dragRandom.Next(-8190,8191));
        validDrags &= moved.Left>=0 && moved.Top>=0 && moved.Right<=4095 && moved.Bottom<=4095 && moved.Left<moved.Right && moved.Top<moved.Bottom;
        if(handle==AreaHandle.Move) validDrags &= moved.Right-moved.Left==original.Right-original.Left && moved.Bottom-moved.Top==original.Bottom-original.Top;
    }
}
Check(validDrags,"9,000 resize/movement scenarios preserve bounds, positive area, and movement size.");

// Export independent deterministic boundary/random cases for Python parity comparison.
if (args.Length == 2 && args[0] == "--export")
{
    var random = new Random(2310);
    var cases = new List<object>();
    for (int i = 0; i < 1000; i++)
    {
        int left = random.Next(4095), top = random.Next(4095);
        var rect = new AreaRect(left, top, random.Next(left + 1, 4096), random.Next(top + 1, 4096));
        var sample = TabletSettings.Defaults();
        sample.TabletArea = sample.ScreenArea = rect;
        sample.InkMode = i % 2 == 0;
        sample.LeftHandMode = i % 3 == 0;
        ushort version = (ushort)((i % 4) switch { 0 => 0, 1 => 0x1802, 2 => 0x1803, _ => 0x2023 });
        cases.Add(new { rect.Left, rect.Top, rect.Right, rect.Bottom, sample.InkMode, sample.LeftHandMode, Version = version,
            Reports = FeatureProtocol.Plan(sample, version).Select(Convert.ToHexString).ToArray() });
    }
    File.WriteAllText(args[1], JsonSerializer.Serialize(cases));
}
if (args.Contains("--devices"))
    Console.WriteLine(JsonSerializer.Serialize(WindowsHid.Enumerate()));
Console.WriteLine($"{checks} core checks passed. Hardware writes were not invoked.");
