IL Repack changelog
====================

2.0.0
-------

* WPF Support
 * Classes (e.g.: converters) from libraries can now be used inside XAML files.
 * User Controls from libraries can now be used inside XAML files.
 * XAML Resource paths in `InitializeComponent()` methods are patched to reference the main assembly instead.
 * Support for styles, theming (`themes/generic.xml`) and inclusion of XAML files (via pack or non-pack URIs).
