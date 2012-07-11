using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using System.Diagnostics;

namespace ILRepacking
{
    class DuplicateHandler
    {
        private readonly IDictionary<string, TypeDefinition> renames = new Dictionary<string, TypeDefinition>();
        internal TypeDefinition GetRenamedType(TypeReference r)
        {
            TypeDefinition other;
            if (renames.TryGetValue(GetTypeKey(r), out other))
            {
                return other;
            }
            return null;
        }
        internal void StoreRenamedType(TypeDefinition orig, TypeDefinition renamed)
        {
            renames[GetTypeKey(orig)] = renamed;
        }
        internal static string GetTypeKey(TypeReference reference)
        {
            var scope = reference.Scope;
            string scopeStr = null;
            if (scope is AssemblyNameReference)
            {
                scopeStr = ((AssemblyNameReference) scope).Name;
            }
            if (scope is ModuleDefinition)
            {
                scopeStr = ((ModuleDefinition)scope).Assembly.Name.Name;
            }
            return scopeStr + reference.FullName;
        }
    }
}
