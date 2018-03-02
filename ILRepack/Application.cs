using ILRepacking.Steps;
using System;

namespace ILRepacking
{
    internal class Application
    {
        [STAThread]
        static int Main(string[] args)
        {
            RepackLogger logger = new RepackLogger();
            RepackOptions options = new RepackOptions(args);
            int returnCode = -1;
            try
            {
                if (options.ShouldShowUsage)
                {
                    Usage();
                    Exit(2);
                }
                logger.ShouldLogVerbose = options.LogVerbose;
                //TODO: Open the Logger before the parse
                if (logger.Open(options.LogFile))
                {
                    options.Log = true;
                }

                ILRepack repack = new ILRepack(options, logger);
                repack.Repack();
                returnCode = 0;
            }
            catch (RepackOptions.InvalidTargetKindException e)
            {
                Console.WriteLine(e.Message);
                Usage();
                Exit(2);
            }
            catch (Exception e)
            {
                logger.Log(e);
                returnCode = 1;
            }
            finally
            {
                logger.Close();
                if (options.PauseBeforeExit)
                {
                    Console.WriteLine("Press Any Key To Continue");
                    Console.ReadKey(true);
                }
            }
            return returnCode;
        }

        static void Usage()
        {
            Console.WriteLine($"IL Repack - assembly merging using Mono.Cecil - Version {typeof(ILRepack).Assembly.GetName().Version.ToString(3)}");
            Console.WriteLine(@"Syntax: ILRepack.exe [Options] /out:<path> <path_to_primary> [<other_assemblies> ...]");
            Console.WriteLine(@" - /help              displays this usage");
            Console.WriteLine(@" - /keyfile:<path>    specifies a keyfile to sign the output assembly");
            Console.WriteLine(@" - /log:<logfile>     enable logging (to a file, if given) (default is disabled)");
            Console.WriteLine(@" - /ver:M.X.Y.Z       target assembly version");
            Console.WriteLine(@" - /union             merges types with identical names into one");
            Console.WriteLine(@" - /ndebug            disables symbol file generation");
            Console.WriteLine(@" - /copyattrs         copy assembly attributes (by default only the primary assembly attributes are copied)");
            Console.WriteLine(@" - /attr:<path>       take assembly attributes from the given assembly file");
            Console.WriteLine(@" - /allowMultiple     when copyattrs is specified, allows multiple attributes (if type allows)");
            Console.WriteLine(@" - /target:kind       specify target assembly kind (library, exe, winexe supported, default is same as first assembly)");
            Console.WriteLine(@" - /targetplatform:P  specify target platform (v1, v1.1, v2, v4 supported)");
            Console.WriteLine(@" - /xmldocs           merges XML documentation as well");
            Console.WriteLine(@" - /lib:<path>        adds the path to the search directories for referenced assemblies (can be specified multiple times)");
            Console.WriteLine(@" - /internalize       sets all types but the ones from the first assembly 'internal'");
            Console.WriteLine(@" - /delaysign         sets the key, but don't sign the assembly");
            Console.WriteLine($" - /noRepackRes       do not add the resource '{ResourcesRepackStep.ILRepackListResourceName}' with all merged assembly names");

            Console.WriteLine(@" - /usefullpublickeyforreferences - NOT IMPLEMENTED");
            Console.WriteLine(@" - /align             - NOT IMPLEMENTED");
            Console.WriteLine(@" - /closed            - NOT IMPLEMENTED");

            Console.WriteLine(@" - /repackdrop:RepackDropAttribute allows dropping members denoted by this attribute name when merging");
            Console.WriteLine(@" - /allowdup:Type     allows the specified type for being duplicated in input assemblies");
            Console.WriteLine(@" - /allowduplicateresources allows to duplicate resources in output assembly (by default they're ignored)");
            Console.WriteLine(@" - /zeropekind        allows assemblies with Zero PeKind (but obviously only IL will get merged)");
            Console.WriteLine(@" - /wildcards         allows (and resolves) file wildcards (e.g. *.dll) in input assemblies");
            Console.WriteLine(@" - /parallel          use as many CPUs as possible to merge the assemblies");
            Console.WriteLine(@" - /pause             pause execution once completed (good for debugging)");
            Console.WriteLine(@" - /index             stores file:line debug information as type/method attributes (requires PDB)");
            Console.WriteLine(@" - /verbose           shows more logs");
            Console.WriteLine(@" - /out:<path>        target assembly path, symbol/config/doc files will be written here as well");
            Console.WriteLine(@" - <path_to_primary>  primary assembly, gives the name, version to the merged one");
            Console.WriteLine(@" - <other_assemblies> ...");
            Console.WriteLine(@"");
            Console.WriteLine(@"Note: for compatibility purposes, all Options are case insensitive, and can be specified using '/', '-' or '--' prefix.");
        }

        static void Exit(int exitCode)
        {
            Environment.Exit(exitCode);
        }
    }
}
