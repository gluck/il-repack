using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text;

namespace ILRepack.IntegrationTests.Helpers
{
    // Shameless copy from the ultimate Rx fu master:
    // https://github.com/paulcbetts/peasant/blob/master/Peasant/Helpers/ObservableProcess.cs
    public class ObservableProcess : IObservable<int>
    {
        readonly AsyncSubject<int> exit = new AsyncSubject<int>();
        readonly object gate = 42;
        readonly ReplaySubject<string> output = new ReplaySubject<string>();
        readonly Process process;
        readonly IObserver<string> input;

        public ObservableProcess(ProcessStartInfo startInfo, bool throwOnNonZeroExitCode = true)
        {
            startInfo.RedirectStandardError = startInfo.RedirectStandardOutput = startInfo.RedirectStandardInput = true;
            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += OnReceived;
            process.ErrorDataReceived += OnReceived;

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            input = Observer.Create<string>(
                x => { process.StandardInput.WriteLine(x); process.StandardInput.Flush(); },
                () => { });

            Observable.Start(() =>
            {
                int exitCode;
                try
                {
                    process.WaitForExit(60 * 1000);
                }
                finally
                {
                    // recreate flush logic from System.Diagnostics.Process
                    WaitUntilEndOfFile("output");
                    WaitUntilEndOfFile("error");

                    exitCode = process.ExitCode;
                    process.OutputDataReceived -= OnReceived;
                    process.ErrorDataReceived -= OnReceived;
                    process.Close();
                }

                output.OnCompleted();

                if (exitCode != 0 && throwOnNonZeroExitCode)
                {
                    var error = string.Join("\n", output.ToArray().First());
                    exit.OnError(new Exception(error));
                }
                else
                {
                    exit.OnNext(exitCode);
                    exit.OnCompleted();
                }
            }, Scheduler.Default);
        }

        public IObserver<string> Input
        {
            get { return input; }
        }

        public IObservable<string> Output
        {
            get { return output; }
        }

        public IDisposable Subscribe(IObserver<int> observer)
        {
            return exit.Subscribe(observer);
        }

        public void Kill()
        {
            process.Kill();
        }

        public int ProcessId
        {
            get { return process.Id; }
        }

        void OnReceived(object s, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (gate)
            {
                output.OnNext(ReparseAsciiDataAsUtf8(e.Data));
            }
        }

        void WaitUntilEndOfFile(string field)
        {
            var fi = process.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null)
            {
                var sr = fi.GetValue(process);
                if (sr != null)
                {
                    var m = sr.GetType().GetMethod("WaitUtilEOF", BindingFlags.NonPublic | BindingFlags.Instance);
                    m.Invoke(sr, null);
                }
            }
        }

        static string ReparseAsciiDataAsUtf8(string input)
        {
            if (String.IsNullOrEmpty(input)) return input;

            var bytes = new byte[input.Length * 2];
            int i = 0;
            foreach (char c in input)
            {
                bytes[i] = (byte)(c & 0xFF);
                i++;

                var msb = (byte)(c & 0xFF00 >> 16);
                if (msb > 0)
                {
                    bytes[i] = msb;
                    i++;
                }
            }

            var ret = Encoding.UTF8.GetString(bytes, 0, i);
            return ret;
        }
    }
}
