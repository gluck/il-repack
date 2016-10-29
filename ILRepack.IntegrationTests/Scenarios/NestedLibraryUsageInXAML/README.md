This test verifies that ILRepack works with user controls from nested libraries used inside XAML.

A nested library user control is defined as a user control that uses another user controls
from another library.

The test is successful if the MainWindow is properly loaded and shown.
The app returns 0 or 1 depending if any exceptions happen during execution.
