using System.Windows;
using Microsoft.Win32;

namespace Smartcrop.Sample.Wpf;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();

        this.DataContext = new ViewModel(this.SelectFile);
    }

    private string SelectFile()
    {
        var dialog = new OpenFileDialog()
        {
            Filter = "Image Files | *.bmp; *.png; *.jpg; *.jpeg;"
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.FileName;
        }

        return null;
    }
}
