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
using System;
using System.Linq;

namespace ILRepacking.Steps
{
    internal class ReferencesRepackStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly bool _keepOtherVersionReferences;

        public ReferencesRepackStep(ILogger logger, IRepackContext repackContext, RepackOptions options)
        {
            _logger = logger;
            _repackContext = repackContext;
            _keepOtherVersionReferences = options.KeepOtherVersionReferences;
        }

        public void Perform()
        {
            _logger.Verbose("Processing references");

            // Add all AssemblyReferences to merged assembly (probably not necessary)
            var targetAssemblyMainModule = _repackContext.TargetAssemblyMainModule;
            if (!_keepOtherVersionReferences)
            {
                ProcessAssemblies(targetAssemblyMainModule);
            }

            _repackContext.LineIndexer.PostRepackReferences();

            // add all module references (pinvoke dlls)
            foreach (var z in _repackContext.MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.ModuleReferences))
            {
                string name = z.Name;
                if (targetAssemblyMainModule.ModuleReferences.All(y => y.Name != name))
                {
                    targetAssemblyMainModule.ModuleReferences.Add(z);
                }
            }
        }

        private void ProcessAssemblies(ModuleDefinition targetAssemblyMainModule)
        {
            var dict = targetAssemblyMainModule.AssemblyReferences.ToDictionary(a => a.Name, a => a);
            foreach (var z in _repackContext.MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.AssemblyReferences))
            {
                string name = z.Name;
                if (!dict.TryGetValue(name, out var asm) || asm.Version < z.Version)
                {
                    if (asm != null)
                    {
                        targetAssemblyMainModule.AssemblyReferences.Remove(asm);
                    }

                    targetAssemblyMainModule.AssemblyReferences.Add(z);
                    dict[name] = z;
                }
            }
        }
    }
}
