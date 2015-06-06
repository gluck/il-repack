using Mono.Cecil;
using System.Collections.Generic;

namespace ILRepacking
{
    public class RepackAssemblyResolver : DefaultAssemblyResolver
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
