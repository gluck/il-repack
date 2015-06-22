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

namespace ILRepacking.Steps
{
    internal class ReferencesFixStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly IRepackImporter _repackImporter;
        private readonly RepackOptions _options;

        public ReferencesFixStep(
            ILogger logger,
            IRepackContext repackContext,
            IRepackImporter repackImporter,
            RepackOptions options)
        {
            _logger = logger;
            _repackContext = repackContext;
            _repackImporter = repackImporter;
            _options = options;
        }

        public void Perform()
        {
            _logger.Info("Fixing references");

            var fixator = new ReferenceFixator(_logger, _repackContext);
            if (_repackContext.PrimaryAssemblyMainModule.EntryPoint != null)
            {
                _repackContext.TargetAssemblyMainModule.EntryPoint = fixator.Fix(
                    _repackImporter.Import(_repackContext.PrimaryAssemblyDefinition.EntryPoint)).Resolve();
            }

            var targetAssemblyMainModule = _repackContext.TargetAssemblyMainModule;

            // this step travels through all TypeRefs & replaces them by matching TypeDefs
            foreach (var r in targetAssemblyMainModule.Types)
            {
                _logger.Verbose($"- Fixing references for type {r}");
                fixator.FixReferences(r);
            }
            foreach (var r in targetAssemblyMainModule.Types)
            {
                fixator.FixMethodVisibility(r);
            }
            fixator.FixReferences(_repackContext.TargetAssemblyDefinition.MainModule.ExportedTypes);
            fixator.FixReferences(_repackContext.TargetAssemblyDefinition.CustomAttributes);
            fixator.FixReferences(_repackContext.TargetAssemblyDefinition.SecurityDeclarations);
            fixator.FixReferences(targetAssemblyMainModule.CustomAttributes);

            // final reference cleanup (Cecil Import automatically added them)
            foreach (AssemblyDefinition asm in _repackContext.MergedAssemblies)
            {
                foreach (var refer in targetAssemblyMainModule.AssemblyReferences.ToArray())
                {
                    // remove all referenced assemblies with same name, as we didn't bother on the version when merging
                    // in case we reference same assemblies with different versions, there might be prior errors if we don't merge the 'largest one'
                    if (_options.KeepOtherVersionReferences ? refer.FullName == asm.FullName : refer.Name == asm.Name.Name)
                    {
                        targetAssemblyMainModule.AssemblyReferences.Remove(refer);
                    }
                }
            }
        }
    }
}
