using System;
using System.IO;
using System.Reflection;
using ILRepacking.Steps;
using Mono.Cecil;

namespace ILRepacking
{
    internal class Application
    {
        [STAThread]
        static int Main(string[] args)
        {
            RepackLogger logger = new RepackLogger();
            RepackOptions options = null;
            int returnCode = -1;
            try
            {
                options = new RepackOptions(args);
                if (options.ShouldShowUsage)
                {
                    Usage();
                    Exit(2);
                }

                ILRepack repack = new ILRepack(options, logger);
                repack.Repack();
                returnCode = 0;
            }
            catch (RepackOptions.InvalidTargetKindException e)
            {
                Console.Error.WriteLine(e.Message);
                Usage();
                Exit(2);
            }
            catch (Exception e)
            {
                string error = e.ToString();
                if (e is AssemblyResolutionException or FileNotFoundException)
                {
                    error = e.Message;
                }

                logger?.LogError(error);
                returnCode = 1;
            }
            finally
            {
                logger?.Close();
                if (options != null && options.PauseBeforeExit)
                {
                    Console.WriteLine("Press Any Key To Continue");
                    Console.ReadKey(true);
                }
            }
            return returnCode;
        }

        static void Usage()
        {
            var assembly = typeof(ILRepack).Assembly;
            var version = 
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
                assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version.ToString() ?? "";
            Console.WriteLine($@"IL Repack {version}
https://github.com/gluck/il-repack

Syntax: ILRepack.exe [Options] /out:<path> <path_to_primary> [<other_assemblies> ...]

 - /help              displays this help
 - @<path>.rsp        response file containing additional arguments, one per line
 - /log:<logfile>     enable logging to a file (default is disabled)
 - /verbose           more detailed logging

 - /out:<path>        target assembly path, symbol/config/doc files will be written here as well
 - <path_to_primary>  primary assembly, gives the name, version to the merged one
 - <other_assemblies> other assemblies to merge with the primary one
 - /wildcards         allows (and resolves) file wildcards (e.g. *.dll) in input assemblies

 - /lib:<path>        path(s) to search directories to resolve referenced assemblies (can be specified multiple times)

 - /target:kind       target assembly kind (library, exe, winexe supported, default is same as primary assembly)
 - /ver:M.X.Y.Z       target assembly version
 - /keyfile:<path>    keyfile to sign the output assembly
 - /delaysign         set the key, but don't sign the assembly

 - /internalize       make all types except in the first assembly 'internal'.
                      Types in the transitive closure of public API remain public.
 - /union             merges types with identical names into one
 - /allowdup:Type     keep duplicates of the specified type, may be specified more than once
 - /allowdup          if no other /allowdup arguments specified, allow all duplicate types
 - /repackdrop:RepackDropAttribute 
                      allows dropping members denoted by this attribute name when merging

 - /copyattrs         copy assembly attributes (by default only the primary assembly attributes are copied)
 - /attr:<path>       take assembly attributes from the given assembly file
 - /allowMultiple     when copyattrs is specified, allows multiple attributes (if type allows)
 - /targetplatform:P  specify target platform (v1, v1.1, v2, v4 supported)

 - /skipconfig        skips merging config files
 - /illink            merge IL Linker files
 - /xmldocs           merges XML documentation as well

 - /allowduplicateresources allows to duplicate resources in output assembly (by default they're ignored)
 - /noRepackRes       do not add the resource '{ResourcesRepackStep.ILRepackListResourceName}' with all merged assembly names
 - /ndebug            disables symbol file generation (omit this if you want symbols and debug information)
 - /zeropekind        allows assemblies with Zero PeKind (but obviously only IL will get merged)
 - /index             stores file:line debug information as type/method attributes (requires PDB)

 - /parallel          use as many CPUs as possible to merge the assemblies
 - /pause             pause execution once completed (good for debugging)

 - /usefullpublickeyforreferences - NOT IMPLEMENTED
 - /align             - NOT IMPLEMENTED
 - /closed            - NOT IMPLEMENTED

Note: for compatibility purposes, all Options are case insensitive, and can be specified using '/', '-' or '--' prefix.");
        }

        static void Exit(int exitCode)
        {
            Environment.Exit(exitCode);
        }
    }
}
