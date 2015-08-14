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

        public ReferencesRepackStep(ILogger logger, IRepackContext repackContext)
        {
            _logger = logger;
            _repackContext = repackContext;
        }

        public void Perform()
        {
            _logger.Info("Processing references");

            // Add all AssemblyReferences to merged assembly (probably not necessary)
            var targetAssemblyMainModule = _repackContext.TargetAssemblyMainModule;

            foreach (var z in _repackContext.MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.AssemblyReferences))
            {
                string name = z.Name;
                if (!_repackContext.MergedAssemblies.Any(y => y.Name.Name == name) && _repackContext.TargetAssemblyDefinition.Name.Name != name)
                {
                    AssemblyNameReference fixedRef = _repackContext.PlatformFixer.FixPlatformVersion(z);
                    if (!targetAssemblyMainModule.AssemblyReferences.Any(y => Equals(y, fixedRef)))
                    {
                        _logger.Verbose("- add reference " + z);
                        targetAssemblyMainModule.AssemblyReferences.Add(fixedRef);
                    }
                }
            }
            _repackContext.LineIndexer.PostRepackReferences();

            // add all module references (pinvoke dlls)
            foreach (var z in _repackContext.MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.ModuleReferences))
            {
                string name = z.Name;
                if (!targetAssemblyMainModule.ModuleReferences.Any(y => y.Name == name))
                {
                    targetAssemblyMainModule.ModuleReferences.Add(z);
                }
            }
        }

        private static bool Equals (byte [] a, byte [] b)
        {
            if (ReferenceEquals (a, b))
                return true;
            if (a == null)
                return false;
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a [i] != b [i])
                    return false;
            return true;
        }

        private static bool Equals<T> (T a, T b) where T : class, IEquatable<T>
        {
            if (ReferenceEquals (a, b))
                return true;
            if (a == null)
                return false;
            return a.Equals (b);
        }

        private static bool Equals (AssemblyNameReference a, AssemblyNameReference b)
        {
            if (ReferenceEquals (a, b))
                return true;
            if (a.Name != b.Name)
                return false;
            if (!Equals (a.Version, b.Version))
                return false;
            if (a.Culture != b.Culture)
                return false;
            if (!Equals (a.PublicKeyToken, b.PublicKeyToken))
                return false;
            // unsure about this one, but there's #41 and duplicate asm references can't really hurt
            if (a.IsRetargetable != b.IsRetargetable)
                return false;
            return true;
        }

    }
}
