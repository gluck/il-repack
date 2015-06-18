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
        static Regex Success = new Regex(@"All Classes and Methods in .* Verified");
        static Regex Failure = new Regex(@"\d+ Error\(s\) Verifying .*");
        public static IObservable<string> Peverify(string workingDirectory, params string[] args)
        {
            // TODO better path finding ?
            // TODO use pedump --verify code,metadata on Mono ?
            var verifierPath = @"C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.6 Tools\peverify.exe";
            var arg = $"\"{verifierPath}\" /NOLOGO {String.Join(" ", args)}";
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
