using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ILRepacking
{
    class RepackLogger : ILogger
    {
        private string outputFile;
        private StreamWriter writer;

        public bool ShouldLogVerbose { get; set; }

        public RepackLogger()
        {
        }

        public void Log(object str)
        {
            string logStr = str.ToString();
            Console.WriteLine(logStr);
            if (writer!= null)
                writer.WriteLine(logStr);
        }

        public bool Open(string outputFile)
        {
            this.outputFile = outputFile;
            bool didOpenWriter = false;
            if (!string.IsNullOrEmpty(outputFile))
            {
                writer = new StreamWriter(outputFile);
                didOpenWriter = true;
            }
            return didOpenWriter;
        }

        public void Close()
        {
            if (outputFile != null)
            {
                writer.Close();
                writer = null;
            }
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
    }
}
