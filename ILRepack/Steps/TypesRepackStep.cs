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
            _logger.Info("Processing types");

            // merge types, this differs between 'primary' and 'other' assemblies regarding internalizing

            foreach (var r in _repackContext.PrimaryAssemblyDefinition.Modules.SelectMany(x => x.Types))
            {
                _logger.Verbose($"- Importing {r} from {r.Module}");
                _repackImporter.Import(r, _repackContext.TargetAssemblyMainModule.Types, false, ShouldRename(r.FullName));
            }

            foreach (var r in _repackContext.OtherAssemblies.SelectMany(x => x.Modules).SelectMany(m => m.Types))
            {
                _logger.Verbose($"- Importing {r} from {r.Module}");
                _repackImporter.Import(r, _repackContext.TargetAssemblyMainModule.Types, ShouldInternalize(r.FullName), ShouldRename(r.FullName));
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
            _logger.Info("Processing exported types");
            foreach (var m in _repackContext.MergedAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                {
                    if (SkipExportedType(r))
                        continue;

                    _repackContext.MappingHandler.StoreExportedType(m, r.FullName, CreateReference(r));
                }
            }

            foreach (var r in _repackContext.PrimaryAssemblyDefinition.Modules.SelectMany(x => x.ExportedTypes))
            {
                _logger.Verbose($"- Importing Exported Type {r} from {r.Scope}");
                _repackImporter.Import(
                    r, targetAssemblyMainModule.ExportedTypes, targetAssemblyMainModule);
            }

            foreach (var m in _repackContext.OtherAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                { 
                    if (!ShouldInternalize(r.FullName) &&
                        !SkipExportedType(r))
                    {
                        _logger.Verbose($"- Importing Exported Type {r} from {m}");
                        _repackImporter.Import(r, targetAssemblyMainModule.ExportedTypes, targetAssemblyMainModule);
                    }
                    else
                    {
                        _logger.Verbose($"- Skipping Exported Type {r} from {m}");
                    }
                }
            }
        }

        /// <summary>
        /// Check if a type's FullName matches a Regex to exclude it from internalizing.
        /// </summary>
        private bool ShouldInternalize(string typeFullName)
        {
            if (!_repackOptions.Internalize)
                return false;

            if (_repackOptions.ExcludeInternalizeMatches.Count == 0)
                return true;

            string withSquareBrackets = "[" + typeFullName + "]";
            foreach (Regex r in _repackOptions.ExcludeInternalizeMatches)
                if (r.IsMatch(typeFullName) || r.IsMatch(withSquareBrackets))
                    return false;

            return true;
        }

        private bool ShouldRename(string typeFullName)
        {
            if (!_repackOptions.RenameNameSpaces)
                return false;

            if (_repackOptions.RenameNameSpacesMatches.Count == 0)
                return true;

            string withSquareBrackets = "[" + typeFullName + "]";
            foreach (Regex r in _repackOptions.RenameNameSpacesMatches.Keys)
                if (r.IsMatch(typeFullName) || r.IsMatch(withSquareBrackets))
                    return true;

            return false;
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
