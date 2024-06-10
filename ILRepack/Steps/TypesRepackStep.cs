//
// Copyright (c) 2011 Francois Valdy
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

using System;
using System.Collections.Generic;
using Mono.Cecil;
using System.Linq;
using System.Text.RegularExpressions;

namespace ILRepacking.Steps
{
    internal class TypesRepackStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly IRepackImporter _repackImporter;
        private readonly RepackOptions _repackOptions;
        private List<TypeDefinition> _allTypes;

        public TypesRepackStep(
            ILogger logger,
            IRepackContext repackContext,
            IRepackImporter repackImporter,
            RepackOptions repackOptions)
        {
            _logger = logger;
            _repackContext = repackContext;
            _repackImporter = repackImporter;
            _repackOptions = repackOptions;

            _allTypes =
                _repackContext.OtherAssemblies.Concat(new[] { _repackContext.PrimaryAssemblyDefinition })
                    .SelectMany(x => x.Modules)
                    .SelectMany(m => m.Types)
                    .ToList();
        }

        public void Perform()
        {
            RepackTypes();
            RepackExportedTypes();
        }

        private void RepackTypes()
        {
            _logger.Verbose("Processing types");

            // merge types, this differs between 'primary' and 'other' assemblies regarding internalizing

            foreach (var r in _repackContext.PrimaryAssemblyDefinition.Modules.SelectMany(x => x.Types))
            {
                _logger.Verbose($"- Importing {r} from {r.Module}");
                _repackImporter.Import(r, _repackContext.TargetAssemblyMainModule.Types, false);
            }

            foreach (var module in _repackContext.OtherAssemblies.SelectMany(x => x.Modules))
            {
                bool internalizeAssembly = ShouldInternalizeAssembly(module.Assembly.Name.Name);
                foreach (var r in module.Types)
                {
                    _logger.Verbose($"- Importing {r} from {r.Module}");
                    _repackImporter.Import(r, _repackContext.TargetAssemblyMainModule.Types, ShouldInternalize(r, internalizeAssembly));
                }
            }
        }

        private bool SkipExportedType(ExportedType type)
        {
            bool parentIsForwarder = type.DeclaringType != null && type.DeclaringType.IsForwarder;
            bool forwarded = type.IsForwarder || parentIsForwarder;

            return forwarded && _allTypes.Any(t => t.FullName == type.FullName);
        }

        private void RepackExportedTypes()
        {
            var targetAssemblyMainModule = _repackContext.TargetAssemblyMainModule;
            _logger.Verbose("Processing exported types");

            foreach (var module in _repackContext.MergedAssemblies.SelectMany(x => x.Modules))
            {
                bool isPrimaryAssembly = module.Assembly == _repackContext.PrimaryAssemblyDefinition;
                bool internalizeAssembly = !isPrimaryAssembly && ShouldInternalizeAssembly(module.Assembly.Name.Name);

                foreach (var exportedType in module.ExportedTypes)
                {
                    bool skipExportedType = SkipExportedType(exportedType);
                    if (skipExportedType)
                    {
                        continue;
                    }

                    if (internalizeAssembly && ShouldInternalize(exportedType.FullName, internalizeAssembly))
                    {
                        continue;
                    }

                    var reference = CreateReference(exportedType);
                    _repackContext.MappingHandler.StoreExportedType(
                        module,
                        exportedType.FullName,
                        reference);

                    _logger.Verbose($"- Importing Exported Type {exportedType} from {exportedType.Scope}");
                    _repackImporter.Import(
                        exportedType,
                        targetAssemblyMainModule.ExportedTypes,
                        targetAssemblyMainModule);
                }
            }
        }

        private bool ShouldInternalizeAssembly(string assemblyShortName)
        {
            bool internalizeAssembly = _repackOptions.InternalizeAssemblies.Contains(assemblyShortName, StringComparer.OrdinalIgnoreCase);

            if (!_repackOptions.Internalize && !internalizeAssembly)
            {
                return false;
            }

            if (_repackOptions.ExcludeInternalizeAssemblies.Contains(assemblyShortName, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Check if a type's FullName matches a Regex to exclude it from internalizing.
        /// </summary>
        private bool ShouldInternalize(string typeFullName, bool internalizeAssembly)
        {
            if (!internalizeAssembly)
            {
                return false;
            }

            if (_repackOptions.ExcludeInternalizeMatches.Count == 0)
            {
                return true;
            }

            string withSquareBrackets = "[" + typeFullName + "]";
            foreach (Regex r in _repackOptions.ExcludeInternalizeMatches)
            {
                if (r.IsMatch(typeFullName) || r.IsMatch(withSquareBrackets))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ShouldInternalize(TypeDefinition type, bool internalizeAssembly)
        {
            if (!internalizeAssembly)
            {
                return false;
            }

            if (_repackOptions.ExcludeInternalizeSerializable && IsSerializableAndPublic(type))
            {
                return false;
            }

            return ShouldInternalize(type.FullName, internalizeAssembly);
        }

        private bool IsSerializableAndPublic(TypeDefinition type)
        {
            if (!type.IsPublic && !type.IsNestedPublic) return false;

            if (type.Attributes.HasFlag(TypeAttributes.Serializable))
                return true;

            if (type.HasCustomAttributes && type.CustomAttributes.Any(IsSerializable))
            {
                return true;
            }

            return type.HasNestedTypes && type.NestedTypes.Any(IsSerializableAndPublic);
        }

        private bool IsSerializable(CustomAttribute attribute)
        {
            var name = attribute.AttributeType.FullName;
            return name == "System.Runtime.Serialization.DataContractAttribute" ||
                   name == "System.ServiceModel.ServiceContractAttribute" ||
                   name == "System.Xml.Serialization.XmlRootAttribute" ||
                   name == "System.Xml.Serialization.XmlTypeAttribute";
        }

        private TypeReference CreateReference(ExportedType type)
        {
            return new TypeReference(type.Namespace, type.Name, _repackContext.TargetAssemblyMainModule, _repackContext.MergeScope(type.Scope))
            {
                DeclaringType = type.DeclaringType != null ? CreateReference(type.DeclaringType) : null,
            };
        }
    }
}
