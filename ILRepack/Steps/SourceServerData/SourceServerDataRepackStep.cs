using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;

namespace ILRepacking.Steps.SourceServerData
{ 
    internal interface ISourceServerDataRepackStep : IRepackStep, IDisposable
    {
        void Write();
    }

    /// <summary>
    /// Get the pdb info from source servers.
    /// </summary>
    internal class SourceServerDataRepackStep : ISourceServerDataRepackStep
    {
        private readonly string _targetPdbFile;

        private readonly IEnumerable<string> _assemblyFiles;

        private string _srcSrv;

        private PdbStr _pdbStr = new PdbStr();

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

        public void Dispose()
        {
            if (_pdbStr != null)
            {
                _pdbStr.Dispose();
                _pdbStr = null;
            }
        }

        private static string PdbPath(string assemblyFile) => Path.ChangeExtension(assemblyFile, ".pdb");
    }

    /// <summary>
    /// Intended for use on platforms where source server is not supported.
    /// </summary>
    internal class NullSourceServerStep : ISourceServerDataRepackStep
    {
        private ILogger _logger;

        public NullSourceServerStep(ILogger logger)
        {
            _logger = logger;
        }

        public void Perform()
        {
            // intentionally blank
        }

        public void Write()
        {
            _logger.Warn("Did not write source server data to output assembly. " +
            "Source server data is only writeable on Windows");
        }

        public void Dispose()
        {
        }
    }
}
