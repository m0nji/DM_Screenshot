using System.Windows;
namespace DMShot.Editor;
public partial class TextPromptWindow : Window
{
    public TextPromptWindow() { InitializeComponent(); Loaded += (_, _) => Input.Focus(); }
    private void Ok(object sender, RoutedEventArgs e) { DialogResult = true; }
    public static string Ask(Window owner)
    {
        var w = new TextPromptWindow { Owner = owner };
        return w.ShowDialog() == true ? w.Input.Text : "";
    }
}
