using System;
using System.Windows;

namespace WPFSampleApplication
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindowOnLoaded(object sender, RoutedEventArgs e)
        {
            if (!MyUserControl.TemplateApplied)
                throw new InvalidOperationException("The template should have been applied to the control");

            if (Title != "Sample Title")
                throw new InvalidOperationException("The window should have had the title 'Sample Title' but had " + Title);
        }
    }
}
