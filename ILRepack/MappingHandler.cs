using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using System.Diagnostics;

namespace ILRepacking
{
    class MappingHandler
    {
        internal class Pair
        {
            readonly string scope;
            readonly string name;

            public Pair(string scope, string name)
            {
                this.scope = scope;
                this.name = name;
            }

            public override int GetHashCode()
            {
                return scope.GetHashCode() + name.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj == this)
                    return true;
                if (!(obj is Pair))
                    return false;
                Pair p = (Pair) obj;
                return p.scope == scope && p.name == name;
            }
        }

        private readonly IDictionary<Pair, TypeDefinition> mappings = new Dictionary<Pair, TypeDefinition>();

        internal TypeDefinition GetRemappedType(TypeReference r)
        {
            TypeDefinition other;
            if (mappings.TryGetValue(GetTypeKey(r), out other))
            {
                return other;
            }
            return null;
        }

        internal void StoreRemappedType(TypeDefinition orig, TypeDefinition renamed)
        {
            mappings[GetTypeKey(orig)] = renamed;
        }

        private static Pair GetTypeKey(TypeReference reference)
        {
            var scope = reference.Scope;
            var scopeStr = GetScopeName(scope);
            return new Pair(scopeStr, reference.FullName);
        }

        internal static string GetScopeName(IMetadataScope scope)
        {
            string scopeStr = null;
            if (scope is AssemblyNameReference)
                scopeStr = ((AssemblyNameReference) scope).Name;
            if (scope is ModuleDefinition)
                scopeStr = ((ModuleDefinition) scope).Assembly.Name.Name;
            return scopeStr;
        }

        internal static string GetScopeFullName(IMetadataScope scope)
        {
            string scopeStr = null;
            if (scope is AssemblyNameReference)
                scopeStr = ((AssemblyNameReference)scope).FullName;
            if (scope is ModuleDefinition)
                scopeStr = ((ModuleDefinition)scope).Assembly.Name.FullName;
            return scopeStr;
        }
    }
}
