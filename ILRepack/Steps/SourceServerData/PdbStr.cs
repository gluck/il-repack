using System;
using System.Diagnostics;
using System.IO;

namespace ILRepacking.Steps.SourceServerData
{
    internal class PdbStr : IDisposable
    {
        private string _pdbStrPath = Path.GetTempFileName();

        public PdbStr()
        {
            using (var resourceStream = typeof(PdbStr).Assembly.GetManifestResourceStream("ILRepacking.pdbstr.exe"))
            using (var fileStream = File.Create(_pdbStrPath))
            {
                resourceStream.CopyTo(fileStream);
            }
        }

        public string Read(string pdb)
        {
            if (!File.Exists(pdb))
            {
                return $"File doesn't exist: {pdb}";
            }

            return Execute($"-r -p:{pdb} -s:srcsrv");
        }

        public void Write(string pdb, string srcsrv)
        {
            var srcsrvFile = Path.GetTempFileName();
            File.WriteAllText(srcsrvFile, srcsrv);
            Execute($"-w -p:{pdb} -s:srcsrv -i:{srcsrvFile}");
            File.Delete(srcsrvFile);
        }

        private string Execute(string arguments)
        {
            var process = ProcessRunner.Run(_pdbStrPath, arguments);
            return process.Output;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            string file = _pdbStrPath;
            _pdbStrPath = null;
            SafeDeleteFile(file);
        }

        private void SafeDeleteFile(string filePath)
        {
            if (filePath != null)
            {
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch { }
            }
        }

        ~PdbStr()
        {
            string file = _pdbStrPath;
            _pdbStrPath = null;
            SafeDeleteFile(file);
        }
    }
}
