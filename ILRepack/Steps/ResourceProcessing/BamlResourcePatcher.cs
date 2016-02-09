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
        private readonly IList<AssemblyDefinition> _otherAssemblies;

        private readonly Dictionary<Type, Action<BamlRecord, AssemblyDefinition>> _nodeProcessors;

        public BamlResourcePatcher(IRepackContext repackContext)
        {
            _mainAssembly = repackContext.PrimaryAssemblyDefinition;
            _otherAssemblies = repackContext.OtherAssemblies;

            //TODO: use dynamic when we upgrade to .NET 4
            _nodeProcessors = new Dictionary<Type, Action<BamlRecord, AssemblyDefinition>>
            {
                { typeof(AssemblyInfoRecord), (r, asm) => ProcessRecord((AssemblyInfoRecord)r) },
                { typeof(PropertyWithConverterRecord), (r, asm) => ProcessRecord((PropertyWithConverterRecord)r, asm) },
                { typeof(XmlnsPropertyRecord), (r, asm) => ProcessRecord((XmlnsPropertyRecord)r) },
                { typeof(TypeInfoRecord), (r, asm) => ProcessRecord((TypeInfoRecord)r) }
            };
        }

        public bool Process(
            AssemblyDefinition containingAssembly, Res resource, ResReader resourceReader, ResourceWriter resourceWriter)
        {
            if (!resource.IsBamlStream)
                return false;

            resource.data = GetProcessedResource(resource, containingAssembly);

            return false;
        }

        private byte[] GetProcessedResource(Res resource, AssemblyDefinition containingAssembly)
        {
            BamlDocument bamlDocument = BamlUtils.FromResourceBytes(resource.data);

            foreach (BamlRecord node in bamlDocument)
            {
                Action<BamlRecord, AssemblyDefinition> recordProcessor;

                if (_nodeProcessors.TryGetValue(node.GetType(), out recordProcessor))
                {
                    recordProcessor(node, containingAssembly);
                }
            }

            //TODO: diminishing return optimisation: remove duplications + update assembly ids

            return BamlUtils.ToResourceBytes(bamlDocument);
        }

        private void ProcessRecord(PropertyWithConverterRecord record, AssemblyDefinition containingAssembly)
        {
            record.Value = XamlResourcePathPatcherStep.PatchPath(
                record.Value,
                _mainAssembly,
                containingAssembly,
                _otherAssemblies);
        }

        private void ProcessRecord(AssemblyInfoRecord record)
        {
            var assemblyName = new System.Reflection.AssemblyName(record.AssemblyFullName);

            var assemblyDefinition = _otherAssemblies.FirstOrDefault(
                asm => asm.Name.Name == assemblyName.Name || asm.Name.FullName == record.AssemblyFullName);

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

            // Make sure it is one of the merged assemblies
            string xmlAssembly = xmlNamespace.Substring(assemblyStart + AssemblyDef.Length);
            if (_mainAssembly.Name.Name != xmlAssembly && _otherAssemblies.All(x => x.Name.Name != xmlAssembly))
                return;

            string xmlNsWithoutAssembly = xmlNamespace.Substring(0, assemblyStart);
            record.XmlNamespace = string.Format("{0}{1}{2}", xmlNsWithoutAssembly, AssemblyDef, _mainAssembly.Name.Name);
        }

        private void ProcessRecord(TypeInfoRecord record)
        {
            record.TypeFullName = RemoveTypeAssemblyInformation(record.TypeFullName);
        }

        public static string RemoveTypeAssemblyInformation(string fullTypeName)
        {
            // ClassLibrary.GenericResourceKey`1[[ClassLibrary.ThemesResourceKey, ClassLibrary, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]
            int genericTypeStartIndex = fullTypeName.IndexOf("[[", StringComparison.Ordinal);
            if (genericTypeStartIndex == -1)
                return fullTypeName;

            int assemblyInfoStartIndex = fullTypeName.IndexOf(',', genericTypeStartIndex);
            int genericTypeEndIndex = fullTypeName.IndexOf("]]", genericTypeStartIndex, StringComparison.Ordinal);

            return fullTypeName.Remove(assemblyInfoStartIndex, genericTypeEndIndex - assemblyInfoStartIndex);
        }
    }
}
