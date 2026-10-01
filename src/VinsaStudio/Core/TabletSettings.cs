using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VinsaStudio.Core;

public sealed record AreaRect
{
    public int Left { get; }
    public int Top { get; }
    public int Right { get; }
    public int Bottom { get; }
    public static AreaRect Full { get; } = new(0, 0, 4095, 4095);

    public AreaRect(int left, int top, int right, int bottom)
    {
        if (new[] { left, top, right, bottom }.Any(v => v is < 0 or > 4095)
            || left >= right || top >= bottom)
            throw new ArgumentException("Mapping coordinates must be 0..4095 with left < right and top < bottom.");
        (Left, Top, Right, Bottom) = (left, top, right, bottom);
    }
}

/// <summary>Edits verified scalar settings without decoding or changing unknown vendor bytes.</summary>
public sealed class TabletSettings
{
    private const string FactoryIni = "[MainItem]\r\nInkMode=1\r\nSmoothness=0\r\nLeftHandMode=0\r\n[Mapping]\r\nScreenMapLeft=0\r\nScreenMapTop=0\r\nScreenMapRight=4095\r\nScreenMapBottom=4095\r\nTabletMapLeft=0\r\nTabletMapTop=0\r\nTabletMapRight=4095\r\nTabletMapBottom=4095\r\n";
    private readonly byte[] original;
    public string? SourcePath { get; }
    public bool InkMode { get; set; }
    public bool LeftHandMode { get; set; }
    public int Smoothness { get; set; }
    public AreaRect TabletArea { get; set; }
    public AreaRect ScreenArea { get; set; }

    private TabletSettings(byte[] bytes, string? path)
    {
        original = (byte[])bytes.Clone();
        SourcePath = path;
        var values = ReadValues(Text(bytes));
        int Number(string section, string key)
        {
            if (!values.TryGetValue(section + "\0" + key, out var value)
                || !Regex.IsMatch(value, "^[0-9]+$")
                || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                throw new FormatException($"[{section}] {key} must be an unsigned INI integer.");
            return number;
        }
        bool Flag(string key) => (values.TryGetValue("MainItem\0" + key, out var value) ? value : "") switch
        {
            "0" => false, "1" => true, _ => throw new FormatException($"[MainItem] {key} must be 0 or 1.")
        };
        AreaRect Rect(string prefix) => new(Number("Mapping", prefix + "Left"), Number("Mapping", prefix + "Top"),
            Number("Mapping", prefix + "Right"), Number("Mapping", prefix + "Bottom"));
        InkMode = Flag("InkMode");
        LeftHandMode = Flag("LeftHandMode");
        Smoothness = Number("MainItem", "Smoothness");
        TabletArea = Rect("TabletMap");
        ScreenArea = Rect("ScreenMap");
    }

    public static TabletSettings Load(string path) => new(File.ReadAllBytes(path), Path.GetFullPath(path));
    public static TabletSettings Defaults() => new(Encoding.ASCII.GetBytes(FactoryIni), null);
    public void Validate()
    {
        if (Smoothness < 0) throw new ArgumentOutOfRangeException(nameof(Smoothness));
        ArgumentNullException.ThrowIfNull(TabletArea);
        ArgumentNullException.ThrowIfNull(ScreenArea);
    }

    // Latin-1 is deliberately used as a one-byte-to-one-character mapping: legacy
    // text and invalid UTF-8 survive untouched, including a possible UTF-8 BOM.
    private static string Text(byte[] bytes) => Encoding.Latin1.GetString(bytes);
    private static string WithoutBom(string line) => line.StartsWith("\u00ef\u00bb\u00bf", StringComparison.Ordinal) ? line[3..] : line;
    private static Dictionary<string, string> ReadValues(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? section = null;
        foreach (var line in Regex.Split(text, @"\r+\n|\n|\r"))
        {
            var clean = WithoutBom(line).Trim();
            if (clean.StartsWith(';') || clean.StartsWith('#') || clean.Length == 0) continue;
            var header = Regex.Match(clean, @"^\[([^\]]+)\]");
            if (header.Success) { section = header.Groups[1].Value; continue; }
            var assignment = Regex.Match(clean, @"^([^=:]+?)\s*[=:]\s*(.*?)\s*$");
            if (section != null && assignment.Success)
            {
                var key = section + "\0" + assignment.Groups[1].Value.Trim();
                if (!values.TryAdd(key, assignment.Groups[2].Value)) throw new FormatException($"Duplicate INI key: {key.Replace('\0', '.')}");
            }
        }
        return values;
    }

    public byte[] ToBytes()
    {
        Validate();
        var changes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["MainItem\0InkMode"] = InkMode ? 1 : 0,
            ["MainItem\0LeftHandMode"] = LeftHandMode ? 1 : 0,
            ["MainItem\0Smoothness"] = Smoothness
        };
        foreach (var (prefix, rect) in new[] { ("TabletMap", TabletArea), ("ScreenMap", ScreenArea) })
            foreach (var (suffix, value) in new[] { ("Left", rect.Left), ("Top", rect.Top), ("Right", rect.Right), ("Bottom", rect.Bottom) })
                changes.Add("Mapping\0" + prefix + suffix, value);
        var lines = Regex.Split(Text(original), @"(\r+\n|\n|\r)");
        string? section = null;
        for (int i = 0; i < lines.Length; i += 2)
        {
            var header = Regex.Match(WithoutBom(lines[i]), @"^\s*\[([^\]]+)\]");
            if (header.Success) { section = header.Groups[1].Value; continue; }
            if (lines[i].TrimStart().StartsWith(';') || lines[i].TrimStart().StartsWith('#')) continue;
            var match = Regex.Match(lines[i], @"^(\s*([^=:]+?)\s*[=:]\s*)(.*?)(\s*)$");
            if (section != null && match.Success && changes.TryGetValue(section + "\0" + match.Groups[2].Value.Trim(), out var value)
                && int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) != value)
                lines[i] = match.Groups[1].Value + value.ToString(CultureInfo.InvariantCulture) + match.Groups[4].Value;
        }
        return Encoding.Latin1.GetBytes(string.Concat(lines));
    }

    public void SaveCopy(string path)
    {
        var target = Path.GetFullPath(path);
        if (SourcePath != null && StringComparer.OrdinalIgnoreCase.Equals(target, SourcePath))
            throw new IOException("Choose a new file; the original vendor settings cannot be overwritten.");
        var bytes = ToBytes();
        // CreateNew also refuses existing destinations and links to the original.
        using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
        stream.Write(bytes);
    }
}
