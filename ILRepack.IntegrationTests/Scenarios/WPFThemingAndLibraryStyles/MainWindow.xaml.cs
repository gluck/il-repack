using System;
using System.Windows;
using System.Windows.Media;

namespace WPFThemingAndLibraryStyles
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (StyledButton.Content.ToString() != "Text set from a style")
                throw new InvalidOperationException("Expected to have text on the styled button");

            var colorBrush = StyledTextBlock.Foreground as SolidColorBrush;
            if (colorBrush == null || colorBrush.Color != Colors.Red)
                throw new InvalidOperationException("Expected to have a red foreground textblock");

            if (!TemplatedDummyUserControl.TemplateApplied)
                throw new InvalidOperationException("The template should have been applied to the control");

            Close();
        }
    }
}
