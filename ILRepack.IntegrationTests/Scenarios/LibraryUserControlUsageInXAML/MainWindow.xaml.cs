using System.Windows;

namespace LibraryUserControlUsageInXAML
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindowLoaded(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
