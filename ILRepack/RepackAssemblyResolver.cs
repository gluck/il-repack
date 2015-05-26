using Mono.Cecil;
using System.Collections.Generic;

namespace ILRepacking
{
    internal class RepackAssemblyResolver : DefaultAssemblyResolver
    {
        public void RegisterAssemblies(List<AssemblyDefinition> mergedAssemblies)
        {
            foreach (var assemblyDefinition in mergedAssemblies)
            {
                RegisterAssembly(assemblyDefinition);
            }
        }
    }
}
