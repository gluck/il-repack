﻿using ICSharpCode.SharpZipLib.Zip;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using NUnit.Framework;

namespace ILRepack.IntegrationTests.NuGet
{
    static class NuGetHelpers
    {
        static NuGetHelpers()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        }

        private static IObservable<byte[]> CreateDownloadObservable(Uri uri)
        {
            return Observable.Create<byte[]>(o => {
                var result = new ReplaySubject<byte[]>();
                var inner = Observable.Using(() => new WebClient(), wc => {
                    var obs = Observable
                        .FromEventPattern<
                            DownloadDataCompletedEventHandler,
                            DownloadDataCompletedEventArgs>(
                                h => wc.DownloadDataCompleted += h,
                                h => wc.DownloadDataCompleted -= h)
                        .Take(1);
                    wc.DownloadDataAsync(uri);
                    return obs;
                }).Subscribe(ep => {
                    if (ep.EventArgs.Cancelled) {
                        result.OnCompleted();
                    } else {
                        if (ep.EventArgs.Error != null) {
                            result.OnError(new Exception($"Failed to download from {uri}", ep.EventArgs.Error));
                        } else {
                            result.OnNext(ep.EventArgs.Result);
                            result.OnCompleted();
                        }
                    }
                }, ex => {
                    result.OnError(ex);
                });
                return new CompositeDisposable(inner, result.Subscribe(o));
            }).Retry(5);
        }

        private static bool IsDllOrExe(Tuple<string, Func<Stream>> tuple)
        {
            return Path.GetExtension(tuple.Item1) == ".dll" || Path.GetExtension(tuple.Item1) == ".exe";
        }
 
        public static IObservable<Tuple<string, Func<Stream>>> GetNupkgAssembliesAsync(Package package)
        {
            return GetNupkgContentAsync(package).Where(IsDllOrExe).Where(package.Matches);
        }
 
        public static IObservable<Tuple<string, Func<Stream>>> GetNupkgContentAsync(Package package)
        {
            var uriString = $"https://www.nuget.org/api/v2/package/{package.Name}/{package.Version}";
            TestContext.WriteLine($"Downloading package {uriString}...");
            var o = CreateDownloadObservable(new Uri(uriString));
            return o.SelectMany(input => {
                return Observable.Create<Tuple<ZipFile, ZipEntry>>(observer => {
                    var z = new ZipFile(new MemoryStream(input)) { IsStreamOwner = true };
                    var sub = z.Cast<ZipEntry>().ToObservable()
                        .Select(ze => Tuple.Create(z, ze))
                        .Subscribe(observer);
                    return new CompositeDisposable() { z, sub };
                });
            })
            .Select(t => Tuple.Create<string, Func<Stream>>(
                t.Item2.Name
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace("%2B", "+"),
                () => t.Item1.GetInputStream(t.Item2)));
        }
    }
}
