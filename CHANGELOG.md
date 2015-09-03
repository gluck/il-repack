IL Repack changelog
====================

2.0.6
-------
* Put public the method retrieving repacked assembly names.

2.0.5
-------
* Bcl+BclAsync now repacks correctly
 * Due to caching, some TypeRefs where incorrectly flagged class instead of valuetype
 * Some original AssemblyReferences were kept in the merged assembly as ExportedType scope or TypeRef scope, this caused issue unless (by luck) the repacked token was the same as original one
* Added ILRepack version in repack log

2.0.4
-------
* When used with copyattrs flag, primary (first) assembly attributes are prefered
  (when the custom attribute doesn't allow multiple)

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
