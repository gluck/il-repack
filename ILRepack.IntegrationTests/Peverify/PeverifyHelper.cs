using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.RegularExpressions;
using ILRepack.IntegrationTests.Helpers;

namespace ILRepack.IntegrationTests.Peverify
{
    public static class PeverifyHelper
    {
        public const string META_E_CA_FRIENDS_SN_REQUIRED = "801311e6";
        public const string VER_E_TOKEN_RESOLVE = "80131869";
        public const string VER_E_TYPELOAD = "801318f3";
        public const string VER_E_STACK_OVERFLOW = "80131856";

        static Regex Success = new Regex(@"All Classes and Methods in .* Verified");
        static Regex Failure = new Regex(@"\d+ Error\(s\) Verifying .*");

        private static string FindVerifier()
        {
            var sdkdir = @"C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\";
            if (!Directory.Exists(sdkdir))
            {
                throw new Exception("Windows SDK not found");
            }

            List<Version> versions = new List<Version>();

            foreach(var dir in Directory.EnumerateDirectories(sdkdir, "NETFX *"))
            {
                var parts = Path.GetFileName(dir)?.Split(' ');

                if(parts == null || parts.Length != 3) continue;

                if (Version.TryParse(parts[1], out var ver))
                {
                    versions.Add(ver);
                }
            }

            if (versions.Count == 0)
            {
                throw new Exception(".NET SDK not found");
            }

            var latest = versions.Max();

            var tools = $"{sdkdir}\\NETFX {latest} Tools\\";

            if (Environment.Is64BitOperatingSystem)
            {
                return $"{tools}\\x64\\peverify.exe";
            }
            return $"{tools}\\peverify.exe";
        }

        public static IObservable<string> Peverify(string workingDirectory, params string[] args)
        {
            // TODO use pedump --verify code,metadata on Mono ?
            var verifierPath = FindVerifier();
            var arg = $"\"{verifierPath}\" /NOLOGO /hresult /md /il {String.Join(" ", args)}";
            var info = new ProcessStartInfo
            {
                CreateNoWindow = true,
                FileName = verifierPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
                Arguments = arg
            };
            return new ObservableProcess(info).Output.Where(s => !Success.IsMatch(s) && !Failure.IsMatch(s));
        }

        public static IObservable<string> ToErrorCodes(this IObservable<string> output)
        {
            return output.SelectMany(e =>
            {
                var i = e.IndexOf("[HRESULT 0x");
                if (i != -1)
                    return Observable.Return(e.Substring(i + 11, 8).ToLowerInvariant());
                i = e.IndexOf("[MD](0x");
                if (i != -1)
                    return Observable.Return(e.Substring(i + 7, 8).ToLowerInvariant());
                i = e.IndexOf("(Error: 0x");
                if (i != -1)
                    return Observable.Return(e.Substring(i + 10, 8).ToLowerInvariant());

                return Observable.Empty<string>();
            }
            ).Distinct();
        }

        private static string FindRegistryValueUnderKey(string registryBaseKeyName, string registryKeyName, RegistryView registryView)
        {
            using (RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
            {
                using (RegistryKey registryKey2 = registryKey.OpenSubKey(registryBaseKeyName))
                {
                    return registryKey2?.GetValue(registryKeyName)?.ToString() ?? string.Empty;
                }
            }
        }

    }
}
