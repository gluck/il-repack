using System;
using System.IO;

namespace ILRepacking
{
    internal class RepackLogger : ILogger, IDisposable
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
            Application.Error(logStr);
            _writer?.WriteLine(logStr);
        }

        public bool Open(string file)
        {
            if (string.IsNullOrEmpty(file))
                return false;
            if (_writer != null)
            {
                return true;
            }

            _outputFile = file;
            _writer = new StreamWriter(_outputFile);
            return true;
        }

        public void Close()
        {
            if (_writer == null)
                return;
            _writer.Flush();
            _writer.Close();
            _writer = null;
        }

        void IDisposable.Dispose()
        {
            Close();
        }

        public void Error(string msg)
        {
            LogError($"ERROR: {msg}");
        }

        public void Warn(string msg)
        {
            msg = $"WARNING: {msg}";
            Application.Write(msg, ConsoleColor.Yellow);
            _writer?.WriteLine(msg);
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
