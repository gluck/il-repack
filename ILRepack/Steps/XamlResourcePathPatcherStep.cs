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

            _logger.Verbose("Processing XAML resource paths ...");
            foreach (var type in types)
            {
                PatchIComponentConnector(type);
                PatchWpfToolkitVersionResourceDictionary(type);
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

        private void PatchIComponentConnector(TypeDefinition type)
        {
            if (!type.Interfaces.Any(t => t.InterfaceType.FullName == "System.Windows.Markup.IComponentConnector"))
                return;

            var initializeMethod = type.Methods.FirstOrDefault(m =>
                m.Name == "InitializeComponent" && m.Parameters.Count == 0);

            if (initializeMethod == null || !initializeMethod.HasBody)
                return;

            _logger.Verbose(" - Patching type " + type.FullName);
            PatchMethod(initializeMethod);
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
                if (otherAssemblies.Any(assembly => TryPatchPath(path, primaryAssembly, assembly, out patchedPath)))
                    return patchedPath;

                return path;
            }

            if (TryPatchPath(path, primaryAssembly, sourceAssembly, out patchedPath))
                return patchedPath;

            if (!path.EndsWith(".xaml"))
                return path;

            // we've got no non-primary assembly knowledge so far,
            // that means it's a relative path in the source assembly -> just add the assembly's name as subdirectory
            // /themes/file.xaml -> /library/themes/file.xaml
            return "/" + sourceAssembly.Name.Name + path;
        }

        private static bool TryPatchPath(
            string path, AssemblyDefinition primaryAssembly, AssemblyDefinition referenceAssembly, out string patchedPath)
        {
            string referenceAssemblyPath = GetAssemblyPath(referenceAssembly);
            string newPath = GetAssemblyPath(primaryAssembly) + "/" + referenceAssembly.Name.Name;

            // /library;component/file.xaml -> /primary;component/library/file.xaml
            patchedPath = path.Replace(referenceAssemblyPath, newPath);

            // if they're modified, we're good!
            return !ReferenceEquals(patchedPath, path);
        }

        private static string GetAssemblyPath(AssemblyDefinition sourceAssembly)
        {
            return string.Format("/{0};component", sourceAssembly.Name.Name);
        }
    }
}
