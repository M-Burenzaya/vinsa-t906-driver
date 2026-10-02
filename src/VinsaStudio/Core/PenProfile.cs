using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VinsaStudio.Core;

public enum PenAction { Shortcut,RightClick,MiddleClick,LeftClick,Disabled }
[Flags] public enum ShortcutModifiers { None=0,Control=1,Shift=2,Alt=4,Windows=8 }

public sealed record PenBinding(PenAction Action,ushort VirtualKey=0,ShortcutModifiers Modifiers=ShortcutModifiers.None)
{
    public void Validate()
    {
        if(!Enum.IsDefined(Action) || ((int)Modifiers & ~15)!=0) throw new ArgumentException("Invalid pen action or modifiers.");
        if(Action==PenAction.Shortcut && (VirtualKey is 0 or >254 || VirtualKey is 16 or 17 or 18 or 91 or 92))
            throw new ArgumentException("Choose a shortcut with a non-modifier key.");
        if(Action!=PenAction.Shortcut && (VirtualKey!=0 || Modifiers!=ShortcutModifiers.None))
            throw new ArgumentException("Mouse actions cannot contain a keyboard shortcut.");
    }
    public string Label => Action switch
    {
        PenAction.RightClick=>"Right click",PenAction.MiddleClick=>"Middle click",PenAction.LeftClick=>"Left click",PenAction.Disabled=>"Disabled",
        _=>string.Join(" + ",new[] {
            Modifiers.HasFlag(ShortcutModifiers.Control)?"Ctrl":null,
            Modifiers.HasFlag(ShortcutModifiers.Shift)?"Shift":null,
            Modifiers.HasFlag(ShortcutModifiers.Alt)?"Alt":null,
            Modifiers.HasFlag(ShortcutModifiers.Windows)?"Win":null,
            KeyName(VirtualKey)
        }.Where(s=>s is not null))
    };
    private static string KeyName(ushort key)=>key switch
    {
        >=65 and <=90 or >=48 and <=57=>((char)key).ToString(),
        >=112 and <=135=>"F"+(key-111),32=>"Space",9=>"Tab",13=>"Enter",27=>"Esc",8=>"Backspace",46=>"Delete",
        37=>"Left",38=>"Up",39=>"Right",40=>"Down",_=>$"VK {key:X2}"
    };
}

public sealed record PenProfile(int Version,PenBinding Upper,PenBinding Lower)
{
    public static PenProfile Defaults => new(1,new(PenAction.Shortcut,90,ShortcutModifiers.Control),new(PenAction.RightClick));
    public void Validate()
    {
        if(Version!=1 || Upper is null || Lower is null) throw new ArgumentException("Unsupported or incomplete pen profile.");
        Upper.Validate();Lower.Validate();
    }
    private static readonly JsonSerializerOptions Options=new() { WriteIndented=true,Converters={new JsonStringEnumConverter()} };
    public static PenProfile Load(string path)
    {
        var profile=JsonSerializer.Deserialize<PenProfile>(File.ReadAllText(path),Options) ?? throw new ArgumentException("Empty pen profile.");
        profile.Validate();return profile;
    }
    public void Save(string path)
    {
        Validate();
        string absolute=Path.GetFullPath(path),directory=Path.GetDirectoryName(absolute)!;
        Directory.CreateDirectory(directory);
        string temporary=Path.Combine(directory,".pen-profile-"+Guid.NewGuid().ToString("N")+".tmp");
        try { File.WriteAllText(temporary,JsonSerializer.Serialize(this,Options));File.Move(temporary,absolute,true); }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
}
