using System.Windows;

namespace Pickets;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow() => InitializeComponent();

    private void GetStarted_Click(object sender, RoutedEventArgs e) => Close();
}
