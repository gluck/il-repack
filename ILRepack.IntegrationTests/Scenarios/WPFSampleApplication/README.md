This test verifies that ILRepack work with: 
* some of the popular WPF libraries (System.Windows.Interactivity, MahApps, Extended.Wpf.Toolkit)
* signed repacked applications

The test is successful if the MainWindow is properly loaded and shown.
The app returns 0 or 1 depending if any exceptions happen during execution.
