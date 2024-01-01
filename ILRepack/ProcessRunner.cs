using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace ILRepacking
{
    public class ProcessRunner
    {
        public class ProcessRunResult
        {
            public Process Process { get; set; }
            public int ExitCode { get; set; }
            public string Output { get; set; }
            public string ErrorOutput { get; set; }
        }

        public static ProcessRunResult Run(string executableName, string arguments = null, string workingDirectory = null)
        {
            var task = RunAsync(executableName, arguments, workingDirectory);
            task.Wait();
            return task.Result;
        }

        public static Task<ProcessRunResult> RunAsync(string executableName, string arguments = null, string workingDirectory = null)
        {
            var processStartInfo = CreateProcessStartInfo(executableName, arguments, workingDirectory);
            return RunAsync(processStartInfo);
        }

        public static Task<ProcessRunResult> RunAsync(ProcessStartInfo processStartInfo)
        {
            var output = new StringBuilder();
            var error = new StringBuilder();

            Process process = Process.Start(processStartInfo);
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    output.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    error.AppendLine(e.Data);
                }
            };

            var result = new ProcessRunResult()
            {
                Process = process
            };

            var taskCompletionSource = new TaskCompletionSource<ProcessRunResult>();

            void Complete()
            {
                result.Output = output.ToString();
                result.ErrorOutput = error.ToString();
                result.ExitCode = process.ExitCode;
                taskCompletionSource.TrySetResult(result);
            }

            process.Exited += (s, e) =>
            {
                Complete();
            };

            if (process.HasExited)
            {
                Complete();
            }
            else
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            return taskCompletionSource.Task;
        }

        private static ProcessStartInfo CreateProcessStartInfo(string filePath, string arguments = null, string workingDirectory = null)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };
            if (!string.IsNullOrEmpty(arguments))
            {
                processStartInfo.Arguments = arguments;
            }

            return processStartInfo;
        }
    }
}