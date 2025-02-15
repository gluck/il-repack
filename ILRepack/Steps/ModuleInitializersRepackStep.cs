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

            var otherModules = _repackContext.OtherAssemblies.SelectMany(x => x.Modules).ToArray();
            MergeModuleInitializers(_repackContext.TargetAssemblyMainModule, otherModules);
        }

        /// <summary>
        /// Checks if there are other module initializers to call from the primary module initializer.
        /// If that is the case the module initializer gets renamed, a new initializer is added, calls all other module initializers
        /// and at the end the original initializer. The other initializers are then re
        /// </summary>
        /// <param name="mainAssembly">Die Assembly, in der der kombinierte Module-Initializer angelegt wird.</param>
        /// <param name="assemblies">Die Assemblys, aus denen die Module-Initializer eingesammelt werden.</param>
        private void MergeModuleInitializers(ModuleDefinition mainModule, IEnumerable<ModuleDefinition> modulesToMerge)
        {
            var anyModuleInitializersToMerge = modulesToMerge.Any(m => m.Types.Any(t => t.Name == "<Module>"));
            if (!anyModuleInitializersToMerge)
            {
                _logger.Verbose("- Found no module initializers to be merged - skip");
                return;
            }

            var mainModuleType = mainModule.Types.FirstOrDefault(t => t.Name == "<Module>");
            if (mainModuleType is null)
            {
                mainModuleType = new TypeDefinition(
                    "",
                    "<Module>",
                    TypeAttributes.NotPublic | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                    mainModule.TypeSystem.Object
                );
                mainModule.Types.Add(mainModuleType);
            }

            var originalMainInitializer = mainModuleType.Methods.FirstOrDefault(m => m.Name == ".cctor");
            if (originalMainInitializer is not null)
            {
                _logger.Verbose($"- Process main module initializer");

                DemoteModuleInitializerMethodToNormalMethod(originalMainInitializer);
            }

            var newMainInitializer = new MethodDefinition(
                ".cctor",
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                mainModule.TypeSystem.Void
            );

            var newIl = newMainInitializer.Body.GetILProcessor();

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

                var call = newIl.Create(OpCodes.Call, mainModule.ImportReference(subInitializer));
                newIl.Append(call);
            }

            if (originalMainInitializer is not null)
            {
                _logger.Verbose($"- Process original primary module initializer of {mainModule.Assembly.Name.Name}");
                var callOriginal = newIl.Create(OpCodes.Call, originalMainInitializer);
                newIl.Append(callOriginal);
            }

            newIl.Append(newIl.Create(OpCodes.Ret));

            mainModuleType.Methods.Add(newMainInitializer);
        }

        private void DemoteModuleInitializerMethodToNormalMethod(MethodDefinition initializer)
        {
            var newName = $"{initializer.Module.Assembly.Name.Name}_ModuleInitializer";

            _logger.Verbose($"  - Rename module initializer of '{initializer.Module.Assembly.Name.Name}' to '{newName}'");

            initializer.Name = newName;
            initializer.IsSpecialName = false;
            initializer.IsRuntimeSpecialName = false;
        }
    }
}
