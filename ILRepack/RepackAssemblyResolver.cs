using Mono.Cecil;
using Mono.Linker;
using System.Collections.Generic;

namespace ILRepacking
{
    public class RepackAssemblyResolver : AssemblyResolver
    {
        public void RegisterAssemblies(IList<AssemblyDefinition> mergedAssemblies)
        {
            foreach (var assemblyDefinition in mergedAssemblies)
            {
                RegisterAssembly(assemblyDefinition);
            }
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters pr)
        {
            if (TargetRepackAssembly != null && name.FullName == TargetRepackAssembly.FullName)
                return TargetRepackAssembly;
            return base.Resolve(name, pr);
        }

        public override IEnumerable<AssemblyDefinition> Assemblies => new[] { TargetRepackAssembly };
        public AssemblyDefinition TargetRepackAssembly { get; set; }
    }
}
