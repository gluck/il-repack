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
        public static IObservable<string> Peverify(string workingDirectory, params string[] args)
        {
            // TODO better path finding ?
            // TODO use pedump --verify code,metadata on Mono ?
            var verifierPath = @"C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.6 Tools\peverify.exe";
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
