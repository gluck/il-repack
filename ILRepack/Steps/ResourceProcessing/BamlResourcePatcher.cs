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

        public BamlResourcePatcher(IRepackContext repackContext)
        {
            _mainAssembly = repackContext.PrimaryAssemblyDefinition;
            _otherAssemblies = repackContext.OtherAssemblies;
        }

        public bool Process(Res resource, AssemblyDefinition containingAssembly, EmbeddedResource embeddedResource, ResReader resourceReader, ResourceWriter resourceWriter)
        {
            if (!resource.IsBamlStream)
                return false;

            resource.data = GetProcessedResource(resource, containingAssembly);

            return false;
        }

        private byte[] GetProcessedResource(Res resource, AssemblyDefinition containingAssembly)
        {
            BamlDocument bamlDocument = BamlUtils.FromResourceBytes(resource.data);

            foreach (dynamic node in bamlDocument)
            {
                ProcessRecord(node, containingAssembly);
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

        private void ProcessRecord(TextWithConverterRecord record, AssemblyDefinition containingAssembly)
        {
            record.Value = XamlResourcePathPatcherStep.PatchPath(
                record.Value,
                _mainAssembly,
                containingAssembly,
                _otherAssemblies);
        }

        private void ProcessRecord(AssemblyInfoRecord record, AssemblyDefinition containingAssembly)
        {
            var assemblyName = new System.Reflection.AssemblyName(record.AssemblyFullName);

            var isMergedAssembly = _otherAssemblies.FirstOrDefault(
                asm => asm.Name.Name == assemblyName.Name || asm.Name.FullName == record.AssemblyFullName) != null;

            // we are interested in the main assembly in order to fix the signing information, when
            // we sign the repacked assembly
            var isMainAssembly = assemblyName.Name == _mainAssembly.Name.Name;

            if (isMergedAssembly || isMainAssembly)
            {
                record.AssemblyFullName = _mainAssembly.Name.Name;
            }
        }

        private void ProcessRecord(XmlnsPropertyRecord record, AssemblyDefinition containingAssembly)
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

        private void ProcessRecord(TypeInfoRecord record, AssemblyDefinition containingAssembly)
        {
            record.TypeFullName = RemoveTypeAssemblyInformation(record.TypeFullName);
        }

        private void ProcessRecord(BamlRecord record, AssemblyDefinition containingAssembly)
        {
        }

        public string RemoveTypeAssemblyInformation(string fullTypeName)
        {
            // ClassLibrary.GenericResourceKey`1[[ClassLibrary.ThemesResourceKey, ClassLibrary, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]
            // DevExpress.Mvvm.UI.Interactivity.EventTriggerBase`1[[System.Windows.DependencyObject, WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35]]
            int genericTypeStartIndex = fullTypeName.IndexOf("[[", StringComparison.Ordinal);
            if (genericTypeStartIndex == -1)
                return fullTypeName;

            int assemblyInfoStartIndex = fullTypeName.IndexOf(',', genericTypeStartIndex) + 1;
            int genericTypeEndIndex = fullTypeName.IndexOf("]]", genericTypeStartIndex, StringComparison.Ordinal);

            string assemblyName = fullTypeName
                .Substring(assemblyInfoStartIndex, genericTypeEndIndex - assemblyInfoStartIndex).Trim();

            if (_otherAssemblies.Any(a => a.FullName == assemblyName))
                return fullTypeName.Replace(assemblyName, _mainAssembly.FullName);
            return fullTypeName;
        }
    }
}
