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
using Fasterflect;
using Mono.Cecil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ILRepacking.Steps.ResourceProcessing
{
    internal class BamlGenerator
    {
        private const int ResourceDictionaryTypeId = 65012;
        private const string ComponentString = ";component/";

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
            document.AddRange(GetMergedDictionariesAttributes());

            document.Add(new ElementStartRecord
            {
                TypeId = ResourceDictionaryTypeId
            });
            document.Add(new XmlnsPropertyRecord
            {
                Prefix = string.Empty,
                XmlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation",
                AssemblyIds = document.OfType<AssemblyInfoRecord>().Select(asm => asm.AssemblyId).ToArray()
            });

            document.AddRange(GetDictionariesList(importedFiles));

            ElementEndRecord lastEndRecord = new ElementEndRecord();
            document.Add(new DeferableContentStartRecord
            {
                Record = lastEndRecord
            });
            document.Add(lastEndRecord);
            document.Add(new DocumentEndRecord());

            return document;
        }

        public void AddMergedDictionaries(
            BamlDocument document, IEnumerable<string> importedFiles)
        {
            BamlRecord mergedDictionaryRecord = document.FirstOrDefault(IsMergedDictionaryAttribute);

            if (mergedDictionaryRecord != null)
            {
                HandleMergedDictionary(document, importedFiles, mergedDictionaryRecord as AttributeInfoRecord);
            }
            else
            {
                if (document.FindIndex(IsResourceDictionaryElementStart) == -1)
                {
                    //TODO: throw? (Let's hope people read the logs ^_^)
                    _logger.Error(string.Format(
                        "Existing 'Themes/generic.xaml' in {0} is *not* a ResourceDictionary. " +
                        "This will prevent proper WPF application merging.", _mainAssemblyName));
                    return;
                }

                int attributeInfosStartIndex = document.FindLastIndex(r => r is AssemblyInfoRecord);
                if (attributeInfosStartIndex == -1)
                {
                    _logger.Error("Invalid BAML detected. (no AssemblyInfoRecord)");
                    return;
                }

                var extraAttributes = GetMergedDictionariesAttributes().ToList();
                AdjustAttributeIds(document, (ushort)extraAttributes.Count);
                document.InsertRange(attributeInfosStartIndex + 1, extraAttributes);

                int defferableRecordIndex = document.FindIndex(r => r is DeferableContentStartRecord);
                if (attributeInfosStartIndex == -1)
                {
                    _logger.Error("Invalid BAML detected. (No DeferableContentStartRecord)");
                }

                document.InsertRange(defferableRecordIndex, GetDictionariesList(importedFiles));
            }
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

        private static void AdjustAttributeIds(BamlDocument document, ushort offset)
        {
            const string AttributeIdPropertyName = "AttributeId";
            var existingAttributeInfoRecords = document.OfType<AttributeInfoRecord>().ToList();

            foreach (var record in document)
            {
                ushort? attributeId = record.TryGetPropertyValue(AttributeIdPropertyName) as ushort?;
                if (attributeId == null ||
                    record is AttributeInfoRecord ||
                    !existingAttributeInfoRecords.Any(r => r.AttributeId == attributeId))
                {
                    continue;
                }

                record.TrySetPropertyValue(AttributeIdPropertyName, (ushort)(attributeId.Value + offset));
            }

            foreach (var attributeInfoRecord in existingAttributeInfoRecords)
            {
                attributeInfoRecord.AttributeId += offset;
            }
        }

        private static IEnumerable<BamlRecord> GetMergedDictionariesAttributes()
        {
            yield return new AttributeInfoRecord
            {
                Name = "MergedDictionaries",
                OwnerTypeId = ResourceDictionaryTypeId,
            };

            yield return new AttributeInfoRecord
            {
                Name = "Source",
                OwnerTypeId = ResourceDictionaryTypeId,
                AttributeId = 1,
            };
        }

        private void HandleMergedDictionary(
            BamlDocument document,
            IEnumerable<string> importedFiles,
            AttributeInfoRecord mergedDictionariesRecord)
        {
            int indexStart = document.FindIndex(
                r => r is PropertyListStartRecord &&
                    ((PropertyListStartRecord)r).AttributeId == mergedDictionariesRecord.AttributeId);

            int insertIndex = indexStart + 1;
            if (document[insertIndex] is LineNumberAndPositionRecord) insertIndex++;

            List<string> existingUris = document.Skip(indexStart)
                .TakeWhile(r => !(r is PropertyListEndRecord))
                .OfType<PropertyWithConverterRecord>()
                .Select(GetFileNameFromPropertyRecord)
                .ToList();

            document.InsertRange(insertIndex, GetImportRecords(importedFiles.Except(existingUris)));
        }

        private static string GetFileNameFromPropertyRecord(PropertyWithConverterRecord record)
        {
            int fileNameStartIndex = record.Value.IndexOf(ComponentString, StringComparison.Ordinal) +
                                     ComponentString.Length;

            return record.Value.Substring(fileNameStartIndex);
        }

        private static bool IsMergedDictionaryAttribute(BamlRecord record)
        {
            AttributeInfoRecord attributeRecord = record as AttributeInfoRecord;
            if (attributeRecord == null)
                return false;

            return attributeRecord.Name.Equals("MergedDictionaries") &&
                   attributeRecord.OwnerTypeId == ResourceDictionaryTypeId;
        }

        private static bool IsResourceDictionaryElementStart(BamlRecord record)
        {
            return record is ElementStartRecord && ((ElementStartRecord)record).TypeId == ResourceDictionaryTypeId;
        }

        private string GetPackUri(string file)
        {
            return string.Format(
                "pack://application:,,,/{0};component/{1}", _mainAssemblyName, file);
        }

        private List<BamlRecord> GetDictionariesList(IEnumerable<string> importedFiles)
        {
            List<BamlRecord> records = new List<BamlRecord>();

            records.Add(new PropertyListStartRecord());
            records.AddRange(GetImportRecords(importedFiles));
            records.Add(new PropertyListEndRecord());

            return records;
        }

        private List<BamlRecord> GetImportRecords(IEnumerable<string> importedFiles)
        {
            List<BamlRecord> records = new List<BamlRecord>();

            foreach (string file in importedFiles)
            {
                records.Add(new ElementStartRecord
                {
                    TypeId = ResourceDictionaryTypeId,
                });
                records.Add(new PropertyWithConverterRecord
                {
                    AttributeId = 1,
                    ConverterTypeId = 64831,
                    Value = GetPackUri(file)
                });
                records.Add(new ElementEndRecord());
            }

            return records;
        }
    }
}
