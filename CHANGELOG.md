# IL Repack changelog

## 2.0.18
### Fixed
* Usage of netstandard2 support layer for .NET 4.6.1-4.7.1
* WPF merging now handles properly libraries built with the new .NET project formats

## 2.0.17
### Added
* /repackdrop:AttributeClass is now a supported argument to allow dropping specific members during merging (#215)
* /renameInternalized is now a supported argument forcing ILRepack to rename all types from other assemblies during repack (#233)

### Fixed
* Usage of delay sign & public key (#222)

## 2.0.16
### Fixed
* WPF Merging handles correctly cases when BAML types are referencing core .NET types

## 2.0.15
### Added
* WPF merging now works with resources (e.g., images) in used libraries

## 2.0.14
### Changed
* Migrated to Cecil 0.10

### Fixed
* WPF merging handles correctly cases when there in no XAML in the merged project

## 2.0.13
### Changed
* Less aggressive attribute cleanup for the main assembly (unchanged for merged ones) (#174)
* Allow proper use of ExcludeFile in Library mode (#185)
* Handle System.Runtime merging (#188)

### Fixed
* PdbStr doesn't work on Unix, skip it on these OS (#176)

### Added
* Support /keycontainer flag (#183)
* More verbose output header for debugging (#187)
* Expose an $(ILRepack) property for nuget consumers (#192)

## 2.0.12
### Added
* SRCSRV data from PDBs are merged for sources available with HTTP. Others are not merged and only the one from the primary assembly is kept.

## 2.0.11
### Fixed
* Signed WPF applications during the repack process now work properly
* Generic-based resource keys can now be used in WPF applications
* UWP applications are now properly merged

## 2.0.9

### Added
* Add set Log level verbose in IlRepack constructor

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

