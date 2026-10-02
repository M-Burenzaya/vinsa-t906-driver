using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VinsaStudio.Controls;
using VinsaStudio.Core;
namespace VinsaStudio;
public partial class PenBindingsWindow : Window
{
    private PenProfile draft;
    private readonly string path;
    private bool upper,updating;
    public event Action<PenProfile>? ProfileSaved;
    private static readonly PenAction[] Actions=[PenAction.Shortcut,PenAction.RightClick,PenAction.MiddleClick,PenAction.LeftClick,PenAction.Disabled];
    public PenBindingsWindow(PenProfile profile,string profilePath,bool selectUpper)
    {
        draft=profile;path=profilePath;upper=selectUpper;updating=true;
        InitializeComponent();NativeWindowTheme.Attach(this);PenArtworkImage.Source=PenArtwork.Load();
        foreach(var action in Actions) ActionChoice.Items.Add(action==PenAction.Shortcut?"Keyboard shortcut":new PenBinding(action).Label);
        ShowBinding();
    }
    private PenBinding Selected => upper?draft.Upper:draft.Lower;
    private void ShowBinding()
    {
        updating=true;
        UpperButton.Background=(Brush)FindResource(upper?"ControlBorder":"ControlSurface");
        LowerButton.Background=(Brush)FindResource(upper?"ControlSurface":"ControlBorder");
        ActionChoice.SelectedIndex=Array.IndexOf(Actions,Selected.Action);
        ShortcutCapture.IsEnabled=Selected.Action==PenAction.Shortcut;
        ShortcutCapture.Text=Selected.Action==PenAction.Shortcut?Selected.Label:"—";
        updating=false;
    }
    private void SetBinding(PenBinding binding)
    {
        binding.Validate();draft=upper?draft with {Upper=binding}:draft with {Lower=binding};
        ProfileStatus.Text="Unsaved profile changes.";ShowBinding();
    }
    public void SelectButton(bool selectUpper) {upper=selectUpper;ShowBinding();}
    public void Observe(string name,string identifier,bool pressed)
        =>InputMonitor.Text=$"Windows: {name} · {(pressed?"pressed":"released")}\n{identifier}";
    private void UpperClick(object sender,RoutedEventArgs e)=>SelectButton(true);
    private void LowerClick(object sender,RoutedEventArgs e)=>SelectButton(false);
    private void ActionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(updating || ActionChoice.SelectedIndex<0) return;
        var action=Actions[ActionChoice.SelectedIndex];
        SetBinding(action==PenAction.Shortcut?new(PenAction.Shortcut,90,ShortcutModifiers.Control):new(action));
    }
    private void CaptureShortcut(object sender,KeyEventArgs e)
    {
        e.Handled=true;
        Key key=e.Key==Key.System?e.SystemKey:e.Key;
        if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed) return;
        int code=KeyInterop.VirtualKeyFromKey(key);
        if(code<=0 || code>254) return;
        ShortcutModifiers modifiers=ShortcutModifiers.None;
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers|=ShortcutModifiers.Control;
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers|=ShortcutModifiers.Shift;
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers|=ShortcutModifiers.Alt;
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers|=ShortcutModifiers.Windows;
        SetBinding(new(PenAction.Shortcut,(ushort)code,modifiers));
    }
    private void ResetClick(object sender,RoutedEventArgs e)=>SetBinding(upper?PenProfile.Defaults.Upper:PenProfile.Defaults.Lower);
    private void SaveClick(object sender,RoutedEventArgs e)
    {
        try {draft.Save(path);ProfileSaved?.Invoke(draft);ProfileStatus.Text="Profile saved. Device assignments have not been changed.";}
        catch(Exception ex) {MessageBox.Show(this,ex.Message,"Profile could not be saved",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
}
