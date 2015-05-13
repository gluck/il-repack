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
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.Linq;

namespace ILRepacking.Steps
{
    //TODO: Maybe we should fix this in *all* (xaml) files?
    internal class XamlResourcePathPatcherStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;

        public XamlResourcePathPatcherStep(ILogger logger, IRepackContext repackContext)
        {
            _logger = logger;
            _repackContext = repackContext;
        }

        public void Perform()
        {
            var types = _repackContext.TargetAssemblyDefinition.Modules.SelectMany(m => m.Types);

            _logger.VERBOSE("Processing XAML resource paths ...");
            foreach (var type in types)
            {
                var initializeMethod = type.Methods.FirstOrDefault(m =>
                    m.Name == "InitializeComponent" && m.Parameters.Count == 0);

                if (initializeMethod == null || !initializeMethod.HasBody)
                    continue;

                _logger.VERBOSE(" - Patching type " + type.FullName);
                PatchMethod(initializeMethod);
            }
        }

        private void PatchMethod(MethodDefinition method)
        {
            foreach (var stringInstruction in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr))
            {
                string path = stringInstruction.Operand as string;
                if (string.IsNullOrEmpty(path))
                    continue;

                stringInstruction.Operand = PatchPath(
                    path, _repackContext.PrimaryAssemblyDefinition, _repackContext.OtherAssemblies);
            }
        }

        public static string PatchPath(string path, AssemblyDefinition mainAssembly, List<AssemblyDefinition> otherAssemblies)
        {
            if (string.IsNullOrEmpty(path) || !(path.StartsWith("/") || path.StartsWith("pack://")))
                return path;

            foreach (var assemblyToReplace in otherAssemblies)
            {
                string patternToReplace = string.Format("/{0};component", assemblyToReplace.Name.Name);

                if (path.Contains(patternToReplace))
                {
                    string newPath = string.Format(
                        "/{0};component/{1}",
                        mainAssembly.Name.Name,
                        assemblyToReplace.Name.Name);

                    return path.Replace(patternToReplace, newPath);
                }
            }

            return path;
        }
    }
}
