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
        private readonly Dictionary<ushort, AssemblyDefinition> _assemblyMappings = new Dictionary<ushort, AssemblyDefinition>();

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
                { typeof(TypeInfoRecord), r => ProcessRecord((TypeInfoRecord)r) },
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

                using (var targetStream = new MemoryStream())
                {
                    BamlWriter.WriteDocument(bamlDocument, targetStream);
                    targetStream.Position = 0;

                    return BitConverter.GetBytes((int)targetStream.Length).Concat(targetStream.ToArray()).ToArray();
                }
            }
        }

        private void ProcessRecord(AssemblyInfoRecord record)
        {
            var assemblyDefinition =  _mergedAssemblies.FirstOrDefault(
                asm => asm.Name.Name == record.AssemblyFullName || asm.Name.FullName == record.AssemblyFullName);

            // not interested in WPF/.NET Framework related assemblies
            if (assemblyDefinition == null)
                return;

            _assemblyMappings[record.AssemblyId] = assemblyDefinition;
        }

        private void ProcessRecord(TypeInfoRecord record)
        {
            AssemblyDefinition recordAssembly = _assemblyMappings[record.AssemblyId];
            if (_mergedAssemblies.Where(asm => asm != _mainAssembly).Contains(recordAssembly))
            {
                record.AssemblyId = GetAssemblyId(_mainAssembly);
            }
        }

        private ushort GetAssemblyId(AssemblyDefinition assemblyDefinition)
        {
            return _assemblyMappings.First(kvp => kvp.Value == assemblyDefinition).Key;
        }
    }
}
