//
// Copyright (c) 2015 Timotei Dolean
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
using Confuser.Renamer.BAML;
using ILRepacking.Steps;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ILRepacking
{
    internal class BamlResourceProcessor
    {
        private readonly AssemblyDefinition _mainAssembly;
        private readonly List<AssemblyDefinition> _mergedAssemblies;
        private readonly Res _resource;

        private readonly Dictionary<Type, Action<BamlRecord>> _nodeProcessors;

        public BamlResourceProcessor(
            AssemblyDefinition mainAssembly,
            IEnumerable<AssemblyDefinition> mergedAssemblies,
            Res resource)
        {
            _mainAssembly = mainAssembly;
            _mergedAssemblies = mergedAssemblies.ToList();
            _resource = resource;

            _nodeProcessors = new Dictionary<Type, Action<BamlRecord>>
            {
                { typeof(AssemblyInfoRecord), r => ProcessRecord((AssemblyInfoRecord)r) },
                { typeof(PropertyWithConverterRecord), r => ProcessRecord((PropertyWithConverterRecord)r) },
                { typeof(XmlnsPropertyRecord), r => ProcessRecord((XmlnsPropertyRecord)r) }
            };
        }

        public byte[] GetProcessedResource()
        {
            byte[] streamBytes = _resource.data.Skip(4).ToArray();
            using (var bamlStream = new MemoryStream(streamBytes))
            {
                BamlDocument bamlDocument = BamlReader.ReadDocument(bamlStream);

                foreach (BamlRecord node in bamlDocument)
                {
                    Action<BamlRecord> recordProcessor;

                    if (_nodeProcessors.TryGetValue(node.GetType(), out recordProcessor))
                    {
                        recordProcessor(node);
                    }
                }

                //TODO: diminishing return optimisation: remove duplications + update assembly ids
                using (var targetStream = new MemoryStream())
                {
                    BamlWriter.WriteDocument(bamlDocument, targetStream);
                    targetStream.Position = 0;

                    return BitConverter.GetBytes((int)targetStream.Length).Concat(targetStream.ToArray()).ToArray();
                }
            }
        }

        private void ProcessRecord(PropertyWithConverterRecord record)
        {
            record.Value = XamlResourcePathPatcherStep.PatchPath(record.Value, _mainAssembly, _mergedAssemblies);
        }

        private void ProcessRecord(AssemblyInfoRecord record)
        {
            var assemblyDefinition =  _mergedAssemblies.FirstOrDefault(
                asm => asm.Name.Name == record.AssemblyFullName ||asm.Name.FullName == record.AssemblyFullName);

            if (assemblyDefinition != null)
            {
                record.AssemblyFullName = _mainAssembly.Name.Name;
            }
        }

        private void ProcessRecord(XmlnsPropertyRecord record)
        {
            string xmlNamespace = record.XmlNamespace;
            const string AssemblyDef = "assembly=";
            int assemblyStart = xmlNamespace.IndexOf(AssemblyDef, StringComparison.Ordinal);
            if (assemblyStart == -1)
                return;

            string xmlNsWithoutAssembly = xmlNamespace.Substring(0, assemblyStart);
            record.XmlNamespace = string.Format("{0}{1}{2}", xmlNsWithoutAssembly, AssemblyDef, _mainAssembly.Name.Name);
        }
    }
}
