Introduction
============

ILRepack is meant at replacing [ILMerge](http://www.microsoft.com/downloads/details.aspx?FamilyID=22914587-B4AD-4EAE-87CF-B14AE6A939B0&displaylang=en) / [Mono.Merge](http://evain.net/blog/articles/2006/11/06/an-introduction-to-mono-merge).

The former being closed-source, impossible to customize, slow, resource consuming and many more.
The later being deprecated, unsupported, and based on an old version of Mono.Cecil.

Here we're using latest (slightly modified) Cecil sources (0.9), you can find the fork [here](https://github.com/gluck/cecil).
Mono.Posix is also required (build only, it gets merged afterwards) for executable bit set on target file.

Downloads
------

You can grab it using [NuGet](http://nuget.org/packages/ILRepack/).

Or if you're old-school (and want to stay like that), this [direct link](http://nuget.org/api/v2/package/ILRepack) will give you the latest nupkg file, which you can open as a zip file.

Syntax
------

A console application has been released (can be used as DLL as well), using same syntax as ILMerge:
<pre>
`Syntax: ILRepack.exe [options] /out:<path> <path_to_primary> [<other_assemblies> ...]`

  - /help              displays this usage
 - /keyfile:<path>    specifies a keyfile to sign the output assembly
 - /log:<logfile>     enable logging (to a file, if given) (default is disabled)
 - /ver:M.X.Y.Z       target assembly version
 - /union             merges types with identical names into one
 - /ndebug            disables symbol file generation
 - /copyattrs         copy assembly attributes (by default only the primary assembly attributes are copied)
 - /attr:<path>       take assembly attributes from the given assembly file
 - /allowMultiple     when copyattrs is specified, allows multiple attributes (if type allows)
 - /target:kind       specify target assembly kind (library, exe, winexe supported, default is same as first assembly)
 - /targetplatform:P  specify target platform (v1, v1.1, v2, v4 supported)
 - /xmldocs           merges XML documentation as well
 - /lib:<path>        adds the path to the search directories for referenced assemblies (can be specified multiple times)
 - /internalize       sets all types but the ones from the first assembly 'internal'
 - /delaysign         sets the key, but don't sign the assembly
 - /usefullpublickeyforreferences - NOT IMPLEMENTED
 - /align             - NOT IMPLEMENTED
 - /closed            - NOT IMPLEMENTED
 - /allowdup:Type     allows the specified type for being duplicated in input assemblies
 - /allowduplicateresources allows to duplicate resources in output assembly (by default they're ignored)
 - /zeropekind        allows assemblies with Zero PeKind (but obviously only IL will get merged)
 - /wildcards         allows (and resolves) file wildcards (e.g. `*`.dll) in input assemblies
 - /parallel          use as many CPUs as possible to merge the assemblies
 - /pause             pause execution once completed (good for debugging)
 - /verbose           shows more logs
 - /out:<path>        target assembly path, symbol/config/doc files will be written here as well
 - <path_to_primary>  primary assembly, gives the name, version to the merged one
 - <other_assemblies> ...

Note: for compatibility purposes, all options can be specified using '/', '-' or '--' prefix.
</pre>

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
