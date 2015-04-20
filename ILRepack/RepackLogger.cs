using System;
using System.IO;

namespace ILRepacking
{
    class RepackLogger : ILogger
    {
        private string outputFile;
        private StreamWriter writer;

        public bool ShouldLogVerbose { get; set; }

        public void Log(object str)
        {
            string logStr = str.ToString();
            Console.WriteLine(logStr);
            if (writer != null)
                writer.WriteLine(logStr);
        }

        public bool Open(string file)
        {
            if (string.IsNullOrEmpty(file))
                return false;
            outputFile = file;
            writer = new StreamWriter(outputFile);
            return true;
        }

        public void Close()
        {
            if (writer == null)
                return;
            writer.Close();
            writer = null;
        }

        public void ERROR(string msg)
        {
            Log("ERROR: " + msg);
        }

        public void WARN(string msg)
        {
            Log("WARN: " + msg);
        }

        public void INFO(string msg)
        {
            Log("INFO: " + msg);
        }

        public void VERBOSE(string msg)
        {
            if (ShouldLogVerbose)
                Log("INFO: " + msg);
        }

        public void DuplicateIgnored(string ignoredType, object ignoredObject)
        {
            // TODO: put on a list and log a summary
            //INFO("Ignoring duplicate " + ignoredType + " " + ignoredObject);
        }
    }
}
