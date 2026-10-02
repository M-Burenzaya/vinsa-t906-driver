using System.ComponentModel;
using System.Windows;
using VinsaStudio.Controls;
namespace VinsaStudio;
public partial class MappingWindow : Window
{
    internal bool AllowClose { get; set; }
    public MappingWindow() { InitializeComponent(); NativeWindowTheme.Attach(this); Closing += KeepDialog; }
    private void KeepDialog(object? sender, CancelEventArgs e) { if (!AllowClose) { e.Cancel=true; Hide(); } }
}
