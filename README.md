[![Build status](https://img.shields.io/appveyor/ci/gluck/il-repack.svg?label=build%20windows)](https://ci.appveyor.com/project/gluck/il-repack) [![NuGet](https://img.shields.io/nuget/v/ILRepack.svg)](https://www.nuget.org/packages/ILRepack/) [![GitHub license](https://img.shields.io/github/license/gluck/il-repack.svg)](http://www.apache.org/licenses/LICENSE-2.0)   
[![Gitter chat](https://img.shields.io/badge/gitter-join%20chat-green.svg)](https://gitter.im/gluck/il-repack)

Introduction
============

ILRepack is meant at replacing [ILMerge](http://www.microsoft.com/downloads/details.aspx?FamilyID=22914587-B4AD-4EAE-87CF-B14AE6A939B0&displaylang=en) / [Mono.Merge](http://evain.net/blog/articles/2006/11/06/an-introduction-to-mono-merge).

The former being ~~closed-source~~ ([now open-sourced](https://github.com/Microsoft/ILMerge)), impossible to customize, slow, resource consuming and many more.
The later being deprecated, unsupported, and based on an old version of Mono.Cecil.

Here we're using latest (slightly modified) Cecil sources (0.11.5), you can find the fork [here](https://github.com/KirillOsenkov/cecil/tree/ilrepack).

Downloads
------

You can grab it using [NuGet](http://nuget.org/packages/ILRepack/).

Or if you're old-school (and want to stay like that), this [direct link](http://nuget.org/api/v2/package/ILRepack) will give you the latest nupkg file, which you can open as a zip file.

Dotnet Tool Installation
-----

ILRepack can now be installed as a `dotnet tool`:

```powershell
PS C:\> dotnet tool install -g dotnet-ilrepack
```

You can then run ILRepack using `ilrepack`.

> [!Note]
> There's no need to use `dotnet ilrepack`, as the way the tool is installed into the `dotnet tool`s path, all you need to do is issue the command `ilrepack`.

Syntax
------

A console application is available (can be used as DLL as well), using same syntax as ILMerge:
```
Syntax: ILRepack.exe [Options] /out:<path> <path_to_primary> [<other_assemblies> ...]

 - /help              displays this help
 - @<path>.rsp        response file containing additional arguments, one per line
 - /log:<logfile>     enable logging to a file (default is disabled)
 - /verbose           more detailed logging

 - /out:<path>        target assembly path, symbol/config/doc files will be written here as well
 - <path_to_primary>  primary assembly, gives the name, version to the merged one
 - <other_assemblies> other assemblies to merge with the primary one
 - /wildcards         allows (and resolves) file wildcards (e.g. *.dll) in input assemblies

 - /lib:<path>        path(s) to search directories to resolve referenced assemblies 
                      (can be specified multiple times).
                      If you get 'unable to resolve assembly' errors specify a path to a directory
                      where the assembly can be found.

 - /target:kind       target assembly kind [library|exe|winexe], default is same as primary assembly
 - /ver:M.X.Y.Z       target assembly version
 - /keyfile:<path>    keyfile to sign the output assembly
 - /keycontainer:<c>  key container
 - /delaysign         set the key, but don't sign the assembly

 - /internalize       make all types except in the first assembly 'internal'.
                      Types in the transitive closure of public API remain public.
 - /internalizeassembly:<path>
                      Internalize a specific assembly name (no extension).
                      May be specified more than once (one per assembly to internalize).
                      If specified, no need to also specify /internalize.
 - /internalize:<exclude_file>
                      Each line is either a regex/ full type name not to internalize
                      or an assembly name not to internalize (.dll extension optional)
 - /renameinternalized
                      rename each internalized type to a new unique name
 - /excludeinternalizeserializable
                      do not internalize types marked as Serializable

 - /allowdup:Type     keep duplicates of the specified type, may be specified more than once
 - /allowdup          if no other /allowdup arguments specified, allow all duplicate types
 - /union             merges types with identical names into one
 - /repackdrop:RepackDropAttribute 
                      allows dropping members denoted by this attribute name when merging
 - /allowduplicateresources 
                      allows to duplicate resources in output assembly (by default they're ignored)
 - /noRepackRes       do not add the resource '{ResourcesRepackStep.ILRepackListResourceName}' with all merged assembly names

 - /copyattrs         copy assembly attributes (by default only the primary assembly attributes are copied)
 - /attr:<path>       take assembly attributes from the given assembly file
 - /allowMultiple     when copyattrs is specified, allows multiple attributes (if type allows)
 - /targetplatform:P  specify target platform (v1, v1.1, v2, v4 supported)
 - /keepotherversionreferences
                      take reference assembly version into account when removing references

 - /preservetimestamp preserve original file PE timestamp
 - /skipconfig        skips merging config files
 - /illink            merge IL Linker files
 - /xmldocs           merges XML documentation as well
 - /ndebug            disables symbol file generation (omit this if you want symbols and debug information)
 - /zeropekind        allows assemblies with Zero PeKind (but obviously only IL will get merged)
 - /index             stores file:line debug information as type/method attributes (requires PDB)

 - /parallel          use as many CPUs as possible to merge the assemblies
 - /pause             pause execution once completed (good for debugging)

 - /usefullpublickeyforreferences - NOT IMPLEMENTED
 - /align             - NOT IMPLEMENTED
 - /closed            - NOT IMPLEMENTED

Note: for compatibility purposes, all Options are case insensitive, and can be specified using '/', '-' or '--' prefix.
```

How to build
------

Builds directly from within Visual Studio 2022, or using msbuild:

```
git clone --recursive https://github.com/gluck/il-repack.git
cd il-repack
msbuild
```

TODO
------
  * Crash-testing
  * Add remaining features from ILMerge (closed / align)
  * Un-fork Cecil
  * Merge import process & reference fixing

DONE
------
  * PDBs & MDBs should be merged (Thanks Simon)
  * Fixed internal method overriding public one which isn't allowed in the same assembly (Simon)
  * Attribute merge (/copyattrs)
  * XML documentation merge
  * Clean command line parameter parsing
  * Add usage / version
  * App.config merge
  * Internalizing types (Simon)
  * Delay signing (Simon)
  * Target platform selection (Simon)
  * Automatic internal type renaming

Sponsoring / Donations
------
If you like this tool and want to express your thanks, you can contribute either time to the project (issue triage or pull-requests) or donate money to the Free Software Foundation.

[![Donate](https://www.gnu.org/graphics/logo-fsf.org-tiny.png)](https://my.fsf.org/donate/)
