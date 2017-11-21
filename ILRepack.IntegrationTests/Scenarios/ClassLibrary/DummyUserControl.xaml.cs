
using System;
using System.Windows;

namespace ClassLibrary
{
    public partial class DummyUserControl
    {
        public DummyUserControl()
        {
            InitializeComponent();

            Loaded += OnLoaded;

            if (Properties.Resources.image.Size.Width <= 0)
            {
                throw new Exception("Image from resource doesn't seem to be loaded! :(");
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
        {
            if (TheImage.ActualWidth <= 0)
            {
                throw new Exception("Image doesn't seem to be loaded! :(");
            }
        }
    }
}
