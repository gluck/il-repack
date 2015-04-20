using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ILRepacking
{
    public class MappingHandler
    {
        internal class Pair
        {
            internal readonly string scope;
            internal readonly string name;

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
        private readonly IDictionary<Pair, TypeReference> exportMappings = new Dictionary<Pair, TypeReference>();

        internal TypeDefinition GetRemappedType(TypeReference r)
        {
            TypeDefinition other;
            if (r.Scope != null && mappings.TryGetValue(GetTypeKey(r), out other))
            {
                return other;
            }
            return null;
        }

        internal void StoreRemappedType(TypeDefinition orig, TypeDefinition renamed)
        {
            if (orig.Scope != null)
            {
                mappings[GetTypeKey(orig)] = renamed;
            }
        }

        internal void StoreExportedType(IMetadataScope scope, String fullName, TypeReference exportedTo)
        {
            if (scope != null)
            {
                exportMappings[GetTypeKey(scope, fullName)] = exportedTo;
            }
        }

        private static Pair GetTypeKey(TypeReference reference)
        {
            return GetTypeKey(reference.Scope, reference.FullName);
        }

        private static Pair GetTypeKey(IMetadataScope scope, String fullName)
        {
            return new Pair(GetScopeName(scope), fullName);
        }

        internal static string GetScopeName(IMetadataScope scope)
        {
            if (scope is AssemblyNameReference)
                return ((AssemblyNameReference)scope).Name;
            if (scope is ModuleDefinition)
                return ((ModuleDefinition) scope).Assembly.Name.Name;
            throw new Exception("Unsupported scope: " + scope);
        }

        internal static string GetScopeFullName(IMetadataScope scope)
        {
            if (scope is AssemblyNameReference)
                return ((AssemblyNameReference)scope).FullName;
            if (scope is ModuleDefinition)
                return ((ModuleDefinition)scope).Assembly.Name.FullName;
            throw new Exception("Unsupported scope: "+ scope);
        }

        public TypeReference GetExportedRemappedType(TypeReference type)
        {
            TypeReference other;
            if (type.Scope != null && exportMappings.TryGetValue(GetTypeKey(type), out other))
            {
                return other;
            }
            return null;
        }

        internal string GetOrigTypeModule(TypeDefinition nt)
        {
            return mappings.Where(p => p.Value == nt).Select(p => p.Key.scope).FirstOrDefault();
        }
    }
}
