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
using System.Linq;
using System.Resources;

namespace ILRepacking.Steps.ResourceProcessing
{
    internal class BamlResourcePatcher : IResProcessor
    {
        private readonly AssemblyDefinition _mainAssembly;
        private readonly List<AssemblyDefinition> _mergedAssemblies;

        private readonly Dictionary<Type, Action<BamlRecord>> _nodeProcessors;

        public BamlResourcePatcher(IRepackContext repackContext)
        {
            _mainAssembly = repackContext.PrimaryAssemblyDefinition;
            _mergedAssemblies = repackContext.MergedAssemblies;

            _nodeProcessors = new Dictionary<Type, Action<BamlRecord>>
            {
                { typeof(AssemblyInfoRecord), r => ProcessRecord((AssemblyInfoRecord)r) },
                { typeof(PropertyWithConverterRecord), r => ProcessRecord((PropertyWithConverterRecord)r) },
                { typeof(XmlnsPropertyRecord), r => ProcessRecord((XmlnsPropertyRecord)r) }
            };
        }

        public bool Process(
            AssemblyDefinition containingAssembly, Res resource, ResReader resourceReader, ResourceWriter resourceWriter)
        {
            if (!resource.IsBamlStream)
                return false;

            resource.data = GetProcessedResource(resource);

            return false;
        }

        private byte[] GetProcessedResource(Res resource)
        {
            BamlDocument bamlDocument = BamlUtils.FromResourceBytes(resource.data);

            foreach (BamlRecord node in bamlDocument)
            {
                Action<BamlRecord> recordProcessor;

                if (_nodeProcessors.TryGetValue(node.GetType(), out recordProcessor))
                {
                    recordProcessor(node);
                }
            }

            //TODO: diminishing return optimisation: remove duplications + update assembly ids

            return BamlUtils.ToResourceBytes(bamlDocument);
        }

        private void ProcessRecord(PropertyWithConverterRecord record)
        {
            record.Value = XamlResourcePathPatcherStep.PatchPath(record.Value, _mainAssembly, _mergedAssemblies);
        }

        private void ProcessRecord(AssemblyInfoRecord record)
        {
            var assemblyDefinition = _mergedAssemblies.FirstOrDefault(
                asm => asm.Name.Name == record.AssemblyFullName || asm.Name.FullName == record.AssemblyFullName);

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
