using System;
using System.IO;

namespace ILRepacking
{
    public class Logger
    {
        private StreamWriter _logFile;

        public Logger(bool logVerbose)
        {
            LogVerbose = logVerbose;
        }

        public bool Log { get; set; }
        public string LogFile { get; set; }
        public bool LogVerbose { get; set; }

        public void AlwaysLog(object str)
        {
            string logStr = str.ToString();
            Console.WriteLine(logStr);
            if (_logFile != null)
                _logFile.WriteLine(logStr);
        }

        public void LogOutput(object str)
        {
            if (Log)
            {
                AlwaysLog(str);
            }
        }

        public void InitializeLogFile()
        {
            if (!string.IsNullOrEmpty(LogFile))
            {
                Log = true;
                _logFile = new StreamWriter(LogFile);
            }
        }

        public void CloseLogFile()
        {
            if (_logFile != null)
            {
                _logFile.Flush();
                _logFile.Close();
                _logFile.Dispose();
                _logFile = null;
            }
        }

        public void ERROR(string msg)
        {
            AlwaysLog("ERROR: " + msg);
        }

        public void WARN(string msg)
        {
            AlwaysLog("WARN: " + msg);
        }

        public void INFO(string msg)
        {
            LogOutput("INFO: " + msg);
        }

        public void VERBOSE(string msg)
        {
            if (LogVerbose)
            {
                LogOutput("INFO: " + msg);
            }
        }
    }
}