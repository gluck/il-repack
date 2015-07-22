IL Repack changelog
====================

2.0.3
-------
* Types fixed during the 'Fixing references' phase are printed in verbose mode.
* InternalsVisibleTo attributes gets cleaned up to allow signed repacked assemblies to be loaded fine.

2.0.0
-------

* WPF Support
 * Classes (e.g.: converters) from libraries can now be used inside XAML files.
 * User Controls from libraries can now be used inside XAML files.
 * XAML Resource paths in `InitializeComponent()` methods are patched to reference the main assembly instead.
 * Support for styles, theming (`themes/generic.xml`) and inclusion of XAML files (via pack or non-pack URIs).
* API has been cleaned up (with potential breaking changes, depending on the usage).
