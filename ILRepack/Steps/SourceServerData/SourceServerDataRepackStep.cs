using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using SourceLink;

namespace ILRepacking.Steps.SourceServerData
{
    internal class SourceServerDataRepackStep : IRepackStep
    {
        private readonly string _targetPdbFile;

        private readonly IEnumerable<string> _assemblyFiles;

        private byte[] _srcSrv;

        public SourceServerDataRepackStep(string targetFile, IEnumerable<string> assemblyFiles)
        {
            if (targetFile == null) throw new ArgumentNullException(nameof(targetFile));
            if (assemblyFiles == null) throw new ArgumentNullException(nameof(assemblyFiles));
            _targetPdbFile = PdbPath(targetFile);
            _assemblyFiles = assemblyFiles;
        }

        public void Perform()
        {
            if (_assemblyFiles.Any())
            {
                var pdbs = _assemblyFiles.Select(PdbPath).ToArray();
                string primaryPdb = pdbs.First();
                if (File.Exists(primaryPdb))
                {
                    _srcSrv = PdbFile.readSrcSrvBytes(primaryPdb);
                    HttpSourceServerDescriptor primaryDescriptor;
                    if(HttpSourceServerDescriptor.TryParse(_srcSrv, out primaryDescriptor))
                    {
                        var otherDescriptors = pdbs
                            .Except(new[] { primaryPdb })
                            .Where(File.Exists)
                            .Select(PdbFile.readSrcSrvBytes)
                            .Select(bytes =>
                                {
                                    HttpSourceServerDescriptor descriptor;
                                    return new
                                        {
                                            Valid = HttpSourceServerDescriptor.TryParse(bytes, out descriptor),
                                            Descriptor = descriptor
                                        };
                                })
                            .Where(tuple => tuple.Valid)
                            .Select(tuple => tuple.Descriptor);
                        _srcSrv = Encoding.UTF8.GetBytes(
                                MergeHttpSourceServerData(primaryDescriptor, otherDescriptors).ToString());
                    }
                }
            }
        }

        internal static HttpSourceServerDescriptor MergeHttpSourceServerData(HttpSourceServerDescriptor primary, IEnumerable<HttpSourceServerDescriptor> others)
        {
            const string Var2Key = "%var2%";
            int version = primary.Version;
            string versionControl = primary.VersionControl;
            var sourceFiles = new[] { primary }.Union(others)
                .Where(descriptor => descriptor.VersionControl == versionControl)
                .SelectMany(descriptor =>
                        descriptor.SourceFiles.Select(file => new { descriptor.Target, file.Path, file.Variable2 }))
                .Select(tuple => new SourceFileDescriptor(tuple.Path, tuple.Target.Replace(Var2Key, tuple.Variable2)));
            return new HttpSourceServerDescriptor(version, versionControl, Var2Key, sourceFiles.ToArray());
        }

        public void Write()
        {
            if (_srcSrv != null)
            {
                PdbFile.writeSrcSrvBytes(_targetPdbFile, _srcSrv);
            }
        }

        private static string PdbPath(string assemblyFile) => Path.ChangeExtension(assemblyFile, ".pdb");
    }
}
