using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VinsaStudio.Controls;
namespace VinsaStudio;
public partial class KeysWindow : Window
{
    internal bool AllowClose { get; set; }
    private readonly string[] labels=["E","B","Ctrl + -","Ctrl + =","[","]","Tab","Space","Ctrl","Alt"];
    private readonly string[] keys=["E","B","-","=","[","]","Tab","Space","None","None"];
    public KeysWindow()
    {
        InitializeComponent();
        NativeWindowTheme.Attach(this);
        for(int i=0;i<labels.Length;i++)
        {
            int index=i;
            var button=new Button { Content=labels[i],Margin=new Thickness(1,0,1,54),FontSize=14 };
            button.Click+=(_,_)=>SelectKey(index);
            KeyButtons.Children.Add(button);
        }
        SelectKey(0);
        Closing+=(_,e)=> { if(!AllowClose) {e.Cancel=true;Hide();} };
    }
    private void SelectKey(int index)
    {
        for(int i=0;i<KeyButtons.Children.Count;i++)
        {
            var button=(Button)KeyButtons.Children[i];
            button.Background=i==index?new SolidColorBrush(Color.FromRgb(72,72,72)):(Brush)FindResource("ControlSurface");
            button.BorderBrush=i==index?(Brush)FindResource("Accent"):(Brush)FindResource("ControlBorder");
        }
        ButtonNumber.Text=(index+1).ToString();
        KeySummary.Text=labels[index];KeyChoice.Items.Clear();KeyChoice.Items.Add(keys[index]);KeyChoice.SelectedIndex=0;
        CtrlKey.IsChecked=index is 2 or 3 or 8;AltKey.IsChecked=index==9;ShiftKey.IsChecked=false;
    }
}
