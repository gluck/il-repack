using DevExpress.Mvvm;
using System.Windows.Input;

namespace LibraryClassUsageInXAML
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;
        }

        public ICommand LoadedCommand => new DelegateCommand(Close);
    }
}
