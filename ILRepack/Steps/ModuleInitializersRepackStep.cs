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

            var modulesToMerge = _repackContext.OtherAssemblies
                .Concat([_repackContext.PrimaryAssemblyDefinition])
                .SelectMany(x => x.Modules)
                .ToArray();
            MergeModuleInitializers(_repackContext.TargetAssemblyMainModule, modulesToMerge);
        }

        /// <summary>
        /// Checks if there are other module initializers to call from the primary module initializer.
        /// If that is the case the module initializer gets renamed, a new initializer is added, calls all other module initializers
        /// and at the end the original initializer. The other initializers are then re
        /// </summary>
        /// <param name="mainAssembly">Die Assembly, in der der kombinierte Module-Initializer angelegt wird.</param>
        /// <param name="assemblies">Die Assemblys, aus denen die Module-Initializer eingesammelt werden.</param>
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
            var newName = $"{initializer.Module.Assembly.Name.Name}_.ModuleInitializer";

            _logger.Verbose($"  - Rename module initializer of '{initializer.Module.Assembly.Name.Name}' to '{newName}'");

            initializer.Name = newName;
            initializer.IsSpecialName = false;
            initializer.IsRuntimeSpecialName = false;
        }
    }
}
