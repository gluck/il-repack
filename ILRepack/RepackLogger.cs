using System;
using System.IO;

namespace ILRepacking
{
    internal class RepackLogger : ILogger
    {
        private string _outputFile;
        private StreamWriter _writer;

        public bool ShouldLogVerbose { get; set; }

        private void Log(string logStr)
        {
            Console.WriteLine(logStr);
            _writer?.WriteLine(logStr);
        }

        public void LogError(object str)
        {
            string logStr = Convert.ToString(str);
            Console.Error.WriteLine(logStr);
            _writer?.WriteLine(logStr);
        }

        public bool Open(string file)
        {
            if (string.IsNullOrEmpty(file))
                return false;
            _outputFile = file;
            _writer = new StreamWriter(_outputFile);
            return true;
        }

        public void Close()
        {
            if (_writer == null)
                return;
            _writer.Close();
            _writer = null;
        }

        public void Error(string msg)
        {
            LogError($"ERROR: {msg}");
        }

        public void Warn(string msg)
        {
            Log($"WARNING: {msg}");
        }

        public void Info(string msg)
        {
            Log($"{msg}");
        }

        public void Verbose(string msg)
        {
            if (ShouldLogVerbose)
                Log($"{msg}");
        }
    }
}
