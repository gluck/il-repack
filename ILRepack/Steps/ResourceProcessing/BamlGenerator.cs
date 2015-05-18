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
using Mono.Collections.Generic;
using System.Collections.Generic;
using System.Linq;

namespace ILRepacking.Steps.ResourceProcessing
{
    internal class BamlGenerator
    {
        private static readonly BamlDocument.BamlVersion BamlVersion =
            new BamlDocument.BamlVersion { Major = 0, Minor = 96 };

        private readonly ILogger _logger;
        private readonly Collection<AssemblyNameReference> _targetAssemblyReferences;
        private readonly string _mainAssemblyName;

        public BamlGenerator(
            ILogger logger,
            Collection<AssemblyNameReference> targetAssemblyReferences,
            AssemblyDefinition mainAssembly)
        {
            _logger = logger;
            _targetAssemblyReferences = targetAssemblyReferences;
            _mainAssemblyName = mainAssembly.Name.Name;
        }

        private void AddAssemblyInfos(BamlDocument document)
        {
            var assemblyNames = new[] { "WindowsBase", "PresentationCore", "PresentationFramework" };
            var references = _targetAssemblyReferences.
                Where(asm => assemblyNames.Any(prefix => asm.Name.Equals(prefix)));

            ushort assemblyId = 0;
            foreach (AssemblyNameReference reference in references)
            {
                document.Add(new AssemblyInfoRecord
                {
                    AssemblyFullName = reference.FullName,
                    AssemblyId = assemblyId
                });

                ++assemblyId;
            }
        }

        public BamlDocument GenerateThemesGenericXaml(IEnumerable<string> importedFiles)
        {
            BamlDocument document = new BamlDocument
            {
                Signature = "MSBAML",
                ReaderVersion = BamlVersion,
                WriterVersion = BamlVersion,
                UpdaterVersion = BamlVersion
            };

            document.Add(new DocumentStartRecord());

            AddAssemblyInfos(document);

            document.Add(new AttributeInfoRecord
            {
                Name = "MergedDictionaries",
                OwnerTypeId = 65012,
            });
            document.Add(new AttributeInfoRecord
            {
                Name = "Source",
                OwnerTypeId = 65012,
                AttributeId = 1,
            });

            document.Add(new ElementStartRecord
            {
                TypeId = 65012
            });
            document.Add(new XmlnsPropertyRecord
            {
                Prefix = string.Empty,
                XmlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation",
                AssemblyIds = document.OfType<AssemblyInfoRecord>().Select(asm => asm.AssemblyId).ToArray()
            });
            document.Add(new PropertyListStartRecord());

            foreach (string file in importedFiles)
            {
                document.Add(new ElementStartRecord
                {
                    TypeId = 65012,
                });
                document.Add(new PropertyWithConverterRecord
                {
                    AttributeId = 1,
                    ConverterTypeId = 64831,
                    Value = GetPackUri(file)
                });
                document.Add(new ElementEndRecord());
            }

            document.Add(new PropertyListEndRecord());

            ElementEndRecord lastEndRecord = new ElementEndRecord();
            document.Add(new DeferableContentStartRecord
            {
                Record = lastEndRecord
            });
            document.Add(lastEndRecord);
            document.Add(new DocumentEndRecord());

            return document;
        }

        private string GetPackUri(string file)
        {
            return string.Format(
                "pack://application:,,,/{0};component/{1}",
                _mainAssemblyName,
                file.Replace(".baml", ".xaml"));
        }
    }
}
