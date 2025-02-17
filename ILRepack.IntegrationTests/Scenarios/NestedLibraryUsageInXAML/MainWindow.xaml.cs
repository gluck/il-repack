using System;
using System.Windows;
using AnotherClassLibrary;

namespace NestedLibraryUsageInXAML
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void MainWindowLoaded(object sender, RoutedEventArgs e)
        {
            await new BclAsyncUsage().GetNumber();

            if (!ClassLibrary.ModuleInitializers.MakeInitialized.IsInitialized &&
                ClassLibrary.ModuleInitializers.MakeInitialized.Counter == 1)
                throw new InvalidOperationException($"ModuleInitializer of '{nameof(ClassLibrary.ModuleInitializers.LibraryModuleInitializer)}' was not executed");

            if (!AnotherClassLibrary.ModuleInitializers.MakeInitialized.IsInitialized &&
                AnotherClassLibrary.ModuleInitializers.MakeInitialized.Counter == 2)
                throw new InvalidOperationException($"ModuleInitializer of '{nameof(AnotherClassLibrary.ModuleInitializers.LibraryModuleInitializer)}' was not executed or not in right order");

            if (Program.Counter != 3)
                throw new InvalidOperationException($"ModuleInitializers of '{nameof(Program)}' were not executed or not in right order");

            Close();
        }
    }
}
