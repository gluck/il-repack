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
using System.Text.RegularExpressions;

namespace ILRepacking.Steps
{
    //TODO: Maybe we should fix this in *all* (xaml) files?
    internal class XamlResourcePathPatcherStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private static readonly Regex VersionRegex = new Regex("v(.?\\d)+;", RegexOptions.IgnoreCase);
        private static readonly Regex AssemblyRegex = new Regex("/([^/]*?);component", RegexOptions.IgnoreCase);

        public XamlResourcePathPatcherStep(ILogger logger, IRepackContext repackContext)
        {
            _logger = logger;
            _repackContext = repackContext;
        }

        public void Perform()
        {
            var relevantTypes = GetTypesWhichMayContainPackUris();

            _logger.Verbose("Processing XAML resource paths ...");
            foreach (var type in relevantTypes)
            {
                PatchWpfPackUrisInClrStrings(type);
                PatchWpfToolkitVersionResourceDictionary(type);
            }
        }

        private IEnumerable<TypeDefinition> GetTypesWhichMayContainPackUris()
        {
            var types = _repackContext.TargetAssemblyDefinition.Modules.SelectMany(m => m.Types);

            var isModuleReferencingWpfMap = new Dictionary<ModuleDefinition, bool>();

            foreach (var type in types)
            {
                var originalModule = _repackContext.MappingHandler.GetOriginalModule(type);
                if (!isModuleReferencingWpfMap.TryGetValue(originalModule, out var isReferencingWpf))
                {
                    isModuleReferencingWpfMap[originalModule] = isReferencingWpf = IsModuleDefinitionReferencingWpf(originalModule);
                }

                if (!isReferencingWpf)
                {
                    continue;
                }

                yield return type;
            }
        }

        private bool IsModuleDefinitionReferencingWpf(ModuleDefinition module)
        {
            // checking for PresentationFramework instead of PresentationCore, as for example
            // AnotherClassLibrary only references PresenationFramework but not PresentationCore
            return module.AssemblyReferences.Any(y => y.Name == "PresentationFramework");
        }

        private void PatchWpfPackUrisInClrStrings(TypeDefinition type)
        {
            foreach (var method in type.Methods.Where(x => x.HasBody))
            {
                PatchMethod(method);
            }
        }

        private void PatchWpfToolkitVersionResourceDictionary(TypeDefinition type)
        {
            // Extended WPF toolkit has a nasty way of including the xamls in the generic.xaml
            // Instead of a simple ResourceDictionary they use a custom one which hardcodes
            // the assembly name and version:
            // <core:VersionResourceDictionary AssemblyName="Xceed.Wpf.Toolkit" SourcePath="Calculator/Themes/Generic.xaml" />
            if (!"Xceed.Wpf.Toolkit.Core.VersionResourceDictionary".Equals(type.FullName))
                return;

            var endInitMethod = type.Methods.FirstOrDefault(m =>
                m.Name == "System.ComponentModel.ISupportInitialize.EndInit");

            if (endInitMethod == null)
            {
                _logger.Warn("Could not find a proper 'EndInit' method for Xceed.Wpf.Toolkit to patch!");
                return;
            }

            PatchWpfToolkitEndInitMethod(endInitMethod);
        }

        private void PatchWpfToolkitEndInitMethod(MethodDefinition method)
        {
            const string ComponentPathString = "{0};v{1};component/{2}";
            foreach (var stringInstruction in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr))
            {
                if (!ComponentPathString.Equals(stringInstruction.Operand as string))
                    continue;

                stringInstruction.Operand =
                    string.Format(
                        "/{0};component/Xceed.Wpf.Toolkit/{{2}}",
                        _repackContext.PrimaryAssemblyDefinition.Name.Name);
            }
        }

        private void PatchMethod(MethodDefinition method)
        {
            foreach (var stringInstruction in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr))
            {
                string path = stringInstruction.Operand as string;
                if (string.IsNullOrEmpty(path))
                    continue;

                var type = method.DeclaringType;
                var originalScope = _repackContext.MappingHandler.GetOrigTypeScope<ModuleDefinition>(type);

                stringInstruction.Operand = PatchPath(
                    path,
                    _repackContext.PrimaryAssemblyDefinition,
                    originalScope.Assembly,
                    _repackContext.OtherAssemblies);
            }
        }

        internal static string PatchPath(
            string path,
            AssemblyDefinition primaryAssembly,
            AssemblyDefinition sourceAssembly,
            IList<AssemblyDefinition> otherAssemblies)
        {
            if (string.IsNullOrEmpty(path) || !(path.StartsWith("/") || path.StartsWith("pack://")))
                return path;

            string patchedPath = path;
            if (primaryAssembly == sourceAssembly)
            {
                if (otherAssemblies.Any(assembly => TryPatchPath(path, primaryAssembly, assembly, otherAssemblies, true, out patchedPath)))
                    return patchedPath;

                return path;
            }

            if (TryPatchPath(path, primaryAssembly, sourceAssembly, otherAssemblies, false, out patchedPath))
                return patchedPath;

            if (!path.EndsWith(".xaml"))
                return path;

            // we've got no non-primary assembly knowledge so far,
            // that means it's a relative path in the source assembly -> just add the assembly's name as subdirectory
            // /themes/file.xaml -> /library/themes/file.xaml
            return "/" + sourceAssembly.Name.Name + path;
        }

        private static bool TryPatchPath(
            string path, 
            AssemblyDefinition primaryAssembly,
            AssemblyDefinition referenceAssembly,
            IList<AssemblyDefinition> otherAssemblies, 
            bool isPrimarySameAsSource,
            out string patchedPath)
        {
            // get rid of potential versions in the path
            // Starting with a new .NET MSBuild version, in case the project is built
            // via a new-format .csproj, the version is appended
            path = VersionRegex.Replace(path, string.Empty);

            // /library;component/file.xaml -> /primary;component/library/file.xaml
            if (isPrimarySameAsSource)
            {
                string referenceAssemblyPath = GetAssemblyPath(referenceAssembly);
                string newPath = GetAssemblyPath(primaryAssembly) + "/" + referenceAssembly.Name.Name;

                patchedPath = path.Replace(referenceAssemblyPath, newPath);
            }
            else
            {
                patchedPath = AssemblyRegex.Replace(path, m =>
                {
                    if (m.Groups.Count == 2)
                    {
                        if (otherAssemblies.Any(a => a.Name.Name == m.Groups[1].Value))
                        {
                            return GetAssemblyPath(primaryAssembly) + "/" + m.Groups[1].Value;
                        }
                        else
                        {
                            return m.Value;
                        }
                    }
                    else
                    {
                        return m.Value;
                    }
                });
            }

            // if they're modified, we're good!
            return !ReferenceEquals(patchedPath, path);
        }

        private static string GetAssemblyPath(AssemblyDefinition sourceAssembly)
        {
            return string.Format("/{0};component", sourceAssembly.Name.Name);
        }
    }
}
