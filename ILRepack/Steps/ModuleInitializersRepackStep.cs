//
// Copyright (c) 2025 David Rettenbacher
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
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ILRepacking.Steps
{
    internal class ModuleInitializersRepackStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;

        public ModuleInitializersRepackStep(
            ILogger logger,
            IRepackContext repackContext,
            RepackOptions repackOptions)
        {
            _logger = logger;
            _repackContext = repackContext;
        }

        public void Perform()
        {
            RepackModuleInitializers();
        }

        private void RepackModuleInitializers()
        {
            _logger.Verbose("Processing module initializers");

            var assemblies = _repackContext.OtherAssemblies
                .Concat([_repackContext.PrimaryAssemblyDefinition])
                .ToHashSet();

            var orderedAssemblies = TopologicalSort(assemblies);

            var modulesToMerge = orderedAssemblies // dependency-assemblies should be deep-first, so the call order is deep-first
                .SelectMany(x => x.Modules);

            MergeModuleInitializers(_repackContext.TargetAssemblyMainModule, modulesToMerge);
        }

        /// <summary>
        /// Checks if there are other module initializers to call from the primary module initializer.
        /// If that is the case, a new initializer is added which calls all found module initializers.
        /// All found initializers are renamed to be unique while still conveying their origin.
        /// </summary>
        /// <param name="targetModule">Target module which gets the new module initializer</param>
        /// <param name="modulesToMerge">Modules which should be scanned for module initializers.</param>
        private void MergeModuleInitializers(ModuleDefinition targetModule, IEnumerable<ModuleDefinition> modulesToMerge)
        {
            var anyModuleInitializersToMerge = modulesToMerge.Any(m =>
                m.Types.Any(t =>
                    t.Name == "<Module>" &&
                    t.Methods.Any(m =>
                        m.IsStatic &&
                        m.Name == ".cctor")));
            if (!anyModuleInitializersToMerge)
            {
                _logger.Verbose("- Found no module initializers to be merged - skip");
                return;
            }

            var targetModuleType = targetModule.Types.FirstOrDefault(t => t.Name == "<Module>");
            if (targetModuleType is null)
            {
                targetModuleType = new TypeDefinition(
                    "",
                    "<Module>",
                    TypeAttributes.NotPublic | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                    targetModule.TypeSystem.Object
                );
                targetModule.Types.Add(targetModuleType);
            }

            var targetInitializer = new MethodDefinition(
                ".cctor",
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                targetModule.TypeSystem.Void
            );

            var newIl = targetInitializer.Body.GetILProcessor();

            foreach (var moduleToMerge in modulesToMerge)
            {
                // search inside the assembly the module initializer type "<Module>"
                var type = moduleToMerge.Types.FirstOrDefault(t => t.Name == "<Module>");
                if (type is null)
                    continue;

                // search its static constructor (the module initializer method)
                var subInitializer = type.Methods.FirstOrDefault(m => m.Name == ".cctor" && m.IsStatic);
                if (subInitializer is null)
                    continue;

                _logger.Verbose($"- Process module initializer of '{moduleToMerge.Assembly.Name.Name}'");

                DemoteModuleInitializerMethodToNormalMethod(subInitializer);

                var call = newIl.Create(OpCodes.Call, targetModule.ImportReference(subInitializer));
                newIl.Append(call);
            }

            newIl.Append(newIl.Create(OpCodes.Ret));

            targetModuleType.Methods.Add(targetInitializer);
        }

        private void DemoteModuleInitializerMethodToNormalMethod(MethodDefinition initializer)
        {
            var newName = $"{initializer.Module.Assembly.Name}_{Path.GetFileNameWithoutExtension(initializer.Module.Name)}_ModuleInitializer";
            newName = newName.Replace(" ", "_").Replace("=", "_").Replace(",", "").Replace(".", "_");

            _logger.Verbose($"  - Rename module initializer of '{initializer.Module.Assembly.Name.Name}' to '{newName}'");

            initializer.Name = newName;
            initializer.IsSpecialName = false;
            initializer.IsRuntimeSpecialName = false;
        }

        private Dictionary<string, AssemblyDefinition> ToDictionarySkipDuplicates(IEnumerable<AssemblyDefinition> assemblies)
        {
            var dictionary = new Dictionary<string, AssemblyDefinition>();
            foreach (var assembly in assemblies)
            {
                var key = assembly.Name.Name;
                if (!dictionary.ContainsKey(key))
                {
                    dictionary[key] = assembly;
                }
                else
                {
                    _logger.Verbose($"- Duplicate key found: {key} - skipping");
                }
            }

            return dictionary;
        }

        private List<AssemblyDefinition> TopologicalSort(HashSet<AssemblyDefinition> assemblies)
        {
            var loadedAssemblies = ToDictionarySkipDuplicates(assemblies); // Ensure quick lookup
            var visited = new HashSet<AssemblyDefinition>();
            var deepFirstAssemblies = new List<AssemblyDefinition>(assemblies.Count);

            _logger.Verbose("- Sort dependencies");

            foreach (var assembly in assemblies)
            {
                if (DepthFirstSearch(assembly))
                {
                    break;
                }
            }

            return deepFirstAssemblies;

            bool DepthFirstSearch(AssemblyDefinition assembly)
            {
                if (!visited.Add(assembly)) // already visited
                    return false;

                foreach (var reference in assembly.MainModule.AssemblyReferences)
                {
                    if (!loadedAssemblies.TryGetValue(reference.Name, out var referencedAsm))
                    {
                        try
                        {
                            referencedAsm = _repackContext.GlobalAssemblyResolver.Resolve(reference);
                            loadedAssemblies[reference.Name] = referencedAsm;

                            _logger.Verbose($"  - Loaded {reference.Name}");
                        }
                        catch
                        {
                            // noop
                        }

                        if (referencedAsm is null)
                        {
                            _logger.Verbose($"- Warning: Could not find {reference.Name}");
                            continue;
                        }
                    }

                    if (DepthFirstSearch(referencedAsm))
                    {
                        return true;
                    }
                }

                if (assemblies.Contains(assembly))
                {
                    deepFirstAssemblies.Add(assembly);
                }

                // found all assemblies yet?
                if (deepFirstAssemblies.Count == assemblies.Count)
                {
                    return true;
                }

                return false;
            }
        }
    }
}
