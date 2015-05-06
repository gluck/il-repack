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
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;

namespace ILRepacking.Steps
{
    internal class AttributesRepackStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly IRepackCopier _repackCopier;
        private readonly RepackOptions _options;

        public AttributesRepackStep(
            ILogger logger,
            IRepackContext repackContext,
            IRepackCopier repackCopier,
            RepackOptions options)
        {
            _logger = logger;
            _repackContext = repackContext;
            _repackCopier = repackCopier;
            _options = options;
        }

        public void Perform()
        {
            var targetAssemblyDefinition = _repackContext.TargetAssemblyDefinition;
            var targetAssemblyMainModule = _repackContext.TargetAssemblyMainModule;

            if (_options.CopyAttributes)
            {
                CleanupAttributes(typeof(CompilationRelaxationsAttribute).FullName, x => x.ConstructorArguments.Count == 1 /* TODO && x.ConstructorArguments[0].Value.Equals(1) */);
                CleanupAttributes(typeof(SecurityTransparentAttribute).FullName, null);
                CleanupAttributes(typeof(SecurityCriticalAttribute).FullName, x => x.ConstructorArguments.Count == 0);
                CleanupAttributes(typeof(AllowPartiallyTrustedCallersAttribute).FullName, x => x.ConstructorArguments.Count == 0);
                CleanupAttributes("System.Security.SecurityRulesAttribute", x => x.ConstructorArguments.Count == 0);
                RemoveAttributes(typeof(InternalsVisibleToAttribute).FullName, ca =>
                {
                    String name = (string)ca.ConstructorArguments[0].Value;
                    int idx;
                    if ((idx = name.IndexOf(", PublicKey=", StringComparison.Ordinal)) != -1)
                    {
                        name = name.Substring(0, idx);
                    }
                    return _repackContext.MergedAssemblies.Any(x => x.Name.Name == name);
                });
                RemoveAttributes(typeof(AssemblyDelaySignAttribute).FullName, null);
                RemoveAttributes(typeof(AssemblyKeyFileAttribute).FullName, null);
                RemoveAttributes(typeof(AssemblyKeyNameAttribute).FullName, null);
                foreach (var ass in _repackContext.MergedAssemblies)
                {
                    _repackCopier.CopyCustomAttributes(ass.CustomAttributes, targetAssemblyDefinition.CustomAttributes, _options.AllowMultipleAssemblyLevelAttributes, null);
                }
                foreach (var mod in _repackContext.MergedAssemblies.SelectMany(x => x.Modules))
                {
                    _repackCopier.CopyCustomAttributes(mod.CustomAttributes, targetAssemblyMainModule.CustomAttributes, _options.AllowMultipleAssemblyLevelAttributes, null);
                }
            }
            else if (_options.AttributeFile != null)
            {
                AssemblyDefinition attributeAsm = AssemblyDefinition.ReadAssembly(_options.AttributeFile, new ReaderParameters(ReadingMode.Immediate) { AssemblyResolver = _options.GlobalAssemblyResolver });
                _repackCopier.CopyCustomAttributes(attributeAsm.CustomAttributes, targetAssemblyDefinition.CustomAttributes, null);
                _repackCopier.CopyCustomAttributes(attributeAsm.CustomAttributes, targetAssemblyMainModule.CustomAttributes, null);
                // TODO: should copy Win32 resources, too
            }
            else
            {
                _repackCopier.CopyCustomAttributes(_repackContext.PrimaryAssemblyDefinition.CustomAttributes, targetAssemblyDefinition.CustomAttributes, null);
                _repackCopier.CopyCustomAttributes(_repackContext.PrimaryAssemblyMainModule.CustomAttributes, targetAssemblyMainModule.CustomAttributes, null);
                // TODO: should copy Win32 resources, too
            }
            _repackCopier.CopySecurityDeclarations(_repackContext.PrimaryAssemblyDefinition.SecurityDeclarations, targetAssemblyDefinition.SecurityDeclarations, null);
        }

        private void CleanupAttributes(string type, Func<CustomAttribute, bool> extra)
        {
            if (!_repackContext.MergedAssemblies.All(ass => ass.CustomAttributes.Any(attr => attr.AttributeType.FullName == type && (extra == null || extra(attr)))))
            {
                if (RemoveAttributes(type, null))
                {
                    _logger.WARN("[" + type + "] attribute wasn't merged because of inconsistency across merged assemblies");
                }
            }
        }

        private bool RemoveAttributes(string attrTypeName, Func<CustomAttribute, bool> predicate)
        {
            bool ret = false;
            foreach (var ass in _repackContext.MergedAssemblies)
            {
                for (int i = 0; i < ass.CustomAttributes.Count; )
                {
                    if (ass.CustomAttributes[i].AttributeType.FullName == attrTypeName && (predicate == null || predicate(ass.CustomAttributes[i])))
                    {
                        ass.CustomAttributes.RemoveAt(i);
                        ret = true;
                        continue;
                    }
                    i++;
                }
            }
            return ret;
        }
    }
}
