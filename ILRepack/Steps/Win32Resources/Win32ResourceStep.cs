//
// Copyright (c) 2018 Alexander Vostres
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ILRepacking.Steps.Win32Resources.PE;
using Mono.Cecil;

namespace ILRepacking.Steps.Win32Resources
{
    class Win32ResourceStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly Dictionary<AssemblyDefinition, int> _aspOffsets;
        private ResourceDirectory _merged;

        public Win32ResourceStep(ILogger logger, IRepackContext repackContext, Dictionary<AssemblyDefinition, int> aspOffsets)
        {
            _logger = logger;
            _repackContext = repackContext;
            _aspOffsets = aspOffsets;
        }

        private ResourceDirectory LoadResources(ModuleDefinition src)
        {
            return RsrcReader.ReadResourceDirectory(src.FileName);
        }
        
        private ResourceDirectory MergeWin32Resources(ResourceDirectory primary)
        {
            if (primary == null)
                return null;
            foreach (var ass in _repackContext.OtherAssemblies)
            {
                MergeDirectory(new List<ResourceEntry>(), primary, ass, LoadResources(ass.MainModule));
            }
            return primary;
        }

        private void MergeDirectory(List<ResourceEntry> parents, ResourceDirectory ret, AssemblyDefinition ass, ResourceDirectory directory)
        {
            foreach (var entry in directory.Entries)
            {
                var exist = ret.Entries.FirstOrDefault(x => entry.Name == null ? entry.Id == x.Id : entry.Name == x.Name);
                if (exist == null)
                    ret.Entries.Add(entry);
                else
                    MergeEntry(parents, exist, ass, entry);
            }
        }

        private void MergeEntry(List<ResourceEntry> parents, ResourceEntry exist, AssemblyDefinition ass, ResourceEntry entry)
        {
            if (exist.Data != null && entry.Data != null)
            {
                if (IsAspResourceEntry(parents, exist))
                {
                    _aspOffsets[ass] = exist.Data.Length;
                    byte[] newData = new byte[exist.Data.Length + entry.Data.Length];
                    Array.Copy(exist.Data, 0, newData, 0, exist.Data.Length);
                    Array.Copy(entry.Data, 0, newData, exist.Data.Length, entry.Data.Length);
                    exist.Data = newData;
                }
                else if (!IsVersionInfoResource(parents, exist))
                {
                    _logger.Warn(string.Format("Duplicate Win32 resource with id={0}, parents=[{1}], name={2} in assembly {3}, ignoring", entry.Id, string.Join(",", parents.Select(p => p.Name ?? p.Id.ToString()).ToArray()), entry.Name, ass.Name));
                }
                return;
            }
            if (exist.Data != null || entry.Data != null)
            {
                _logger.Warn("Inconsistent Win32 resources, ignoring");
                return;
            }
            parents.Add(exist);
            MergeDirectory(parents, exist.Directory, ass, entry.Directory);
            parents.RemoveAt(parents.Count - 1);
        }

        private static bool IsAspResourceEntry(List<ResourceEntry> parents, ResourceEntry exist)
        {
            return exist.Id == 101 && parents.Count == 1 && parents[0].Id == 3771;
        }

        private static bool IsVersionInfoResource(List<ResourceEntry> parents, ResourceEntry exist)
        {
            return exist.Id == 0 && parents.Count == 2 && parents[0].Id == 16 && parents[1].Id == 1;
        }

        public void Perform()
        {
            _logger.Info("Merging Win32 resources");
            var resources = LoadResources(_repackContext.PrimaryAssemblyMainModule);
            _merged = MergeWin32Resources(resources);
        }

        public void Patch(string outFile)
        {
            if (_merged.Entries.Count == 0)
            {
                return;
            }

            _logger.Info("Patching Win32 resources");

            var ms = new MemoryStream();
            using (var f = File.Open(outFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                f.CopyTo(ms);
            }
            ms.Position = 0;


            var reader = new ImageReader(ms);

            using (var file = File.Open(outFile, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            {
                var writer = new ImageWriter(reader, file);
                writer.Prepare(_merged);
                writer.Write();
            }
        }
    }
}
