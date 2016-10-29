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
                _logger.Verbose("- Importing " + r);
                _repackImporter.Import(r, _repackContext.TargetAssemblyMainModule.Types, false);
            }
            foreach (var m in _repackContext.OtherAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.Types)
                {
                    _logger.Verbose("- Importing " + r);
                    _repackImporter.Import(r, _repackContext.TargetAssemblyMainModule.Types, ShouldInternalize(r.FullName));
                }
            }
        }

        private void RepackExportedTypes()
        {
            var targetAssemblyMainModule = _repackContext.TargetAssemblyMainModule;
            _logger.Info("Processing types");
            foreach (var m in _repackContext.MergedAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                {
                    _repackContext.MappingHandler.StoreExportedType(m, r.FullName, CreateReference(r));
                }
            }
            foreach (var r in _repackContext.PrimaryAssemblyDefinition.Modules.SelectMany(x => x.ExportedTypes))
            {
                _logger.Verbose("- Importing Exported Type" + r);
                _repackImporter.Import(r, targetAssemblyMainModule.ExportedTypes, targetAssemblyMainModule);
            }
            foreach (var m in _repackContext.OtherAssemblies.SelectMany(x => x.Modules))
            {
                foreach (var r in m.ExportedTypes)
                {
                    if (!ShouldInternalize(r.FullName))
                    {
                        _logger.Verbose("- Importing Exported Type " + r);
                        _repackImporter.Import(r, targetAssemblyMainModule.ExportedTypes, targetAssemblyMainModule);
                    }
                    else
                    {
                        _logger.Verbose("- Skipping Exported Type " + r);
                    }
                }
            }
        }

        /// <summary>
        /// Check if a type's FullName matches a Regex to exclude it from internalizing.
        /// </summary>
        private bool ShouldInternalize(string typeFullName)
        {
            if (_repackOptions.ExcludeInternalizeMatches == null)
            {
                return _repackOptions.Internalize;
            }
            string withSquareBrackets = "[" + typeFullName + "]";
            foreach (Regex r in _repackOptions.ExcludeInternalizeMatches)
                if (r.IsMatch(typeFullName) || r.IsMatch(withSquareBrackets))
                    return false;
            return true;
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
