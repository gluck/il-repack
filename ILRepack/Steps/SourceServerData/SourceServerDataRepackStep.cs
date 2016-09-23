using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;

namespace ILRepacking.Steps.SourceServerData
{
    internal class SourceServerDataRepackStep : IRepackStep
    {
        private readonly string _targetPdbFile;

        private readonly IEnumerable<string> _assemblyFiles;

        private string _srcSrv;

        private readonly PdbStr _pdbStr = new PdbStr();

        public SourceServerDataRepackStep(string targetFile, IEnumerable<string> assemblyFiles)
        {
            Contract.Assert(targetFile != null);
            Contract.Assert(assemblyFiles != null);
            _targetPdbFile = PdbPath(targetFile);
            _assemblyFiles = assemblyFiles;
        }

        public void Perform()
        {
            var srcsrvValues = _assemblyFiles
                .Select(PdbPath)
                .Where(File.Exists)
                .Select(_pdbStr.Read)
                .ToArray();
            var descriptors = srcsrvValues.Select(srcsrv =>
                {
                    HttpSourceServerDescriptor descriptor;
                    return new
                        {
                            Valid = HttpSourceServerDescriptor.TryParse(srcsrv, out descriptor),
                            Descriptor = descriptor
                        };
                })
                .Where(tuple => tuple.Valid)
                .Select(tuple => tuple.Descriptor)
                .ToArray();
            _srcSrv = descriptors.Any()
                ? descriptors.First().MergeWith(descriptors.Skip(1)).ToString()
                : srcsrvValues.FirstOrDefault();
        }

        public void Write()
        {
            if (_srcSrv == null) return;
            _pdbStr.Write(_targetPdbFile, _srcSrv);
        }

        private static string PdbPath(string assemblyFile) => Path.ChangeExtension(assemblyFile, ".pdb");
    }
}
