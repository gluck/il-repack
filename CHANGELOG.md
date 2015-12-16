# IL Repack changelog

## 2.0.10

## 2.0.9

### Added
* Add set Log level verbose in IlRepack constructor.

### Fixed
* Indirect xaml dependency merge now copes with conflicting versions

### Changed
* Bumped cecil version

## 2.0.6

### Fixed
* Fixed regression since 2.0.4 that prevented proper merging of resources in dependent assemblies.
 * Because of this, no BAML resources were added from dependencies into the merged result.

### Changed
* Put public the method retrieving repacked assembly names.

## 2.0.5

### Fixed
* Bcl+BclAsync now repacks correctly
 * Due to caching, some TypeRefs where incorrectly flagged class instead of valuetype
 * Some original AssemblyReferences were kept in the merged assembly as ExportedType scope or TypeRef scope, this caused issue unless (by luck) the repacked token was the same as original one

### Added
* Added ILRepack version in repack log

## 2.0.4

### Changed
* When used with copyattrs flag, primary (first) assembly attributes are prefered
  (when the custom attribute doesn't allow multiple)

## 2.0.3

### Changed
* Types fixed during the 'Fixing references' phase are printed in verbose mode.
* InternalsVisibleTo attributes gets cleaned up to allow signed repacked assemblies to be loaded fine.

## 2.0.0

### Added
* WPF Support
 * Classes (e.g.: converters) from libraries can now be used inside XAML files.
 * User Controls from libraries can now be used inside XAML files.
 * XAML Resource paths in `InitializeComponent()` methods are patched to reference the main assembly instead.
 * Support for styles, theming (`themes/generic.xml`) and inclusion of XAML files (via pack or non-pack URIs).

### Changed
* API has been cleaned up (with potential breaking changes, depending on the usage).
