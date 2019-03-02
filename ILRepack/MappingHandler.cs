using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Fasterflect;
using Mono.Cecil.Metadata;

namespace ILRepacking
{
    internal class MappingHandler
    {
        struct Pair : IEquatable<Pair>
        {
            readonly string scope;
            readonly string name;
            public readonly IMetadataScope MetadataScope;

            public Pair(string scope, string name, IMetadataScope metadataScope)
            {
                this.scope = scope;
                this.name = name;
                MetadataScope = metadataScope;
            }

            public override int GetHashCode()
            {
                return scope.GetHashCode() + name.GetHashCode();
            }

            public bool Equals(Pair p)
            {
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
            return new Pair(GetScopeName(scope), fullName, scope);
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

        private TypeReference GetRootReference(TypeReference type)
        {
            TypeReference other;
            if (type.Scope != null && exportMappings.TryGetValue(GetTypeKey(type), out other))
            {
                var next = GetRootReference(other);
                return next ?? other;
            }
            return null;
        }

        public TypeReference GetExportedRemappedType(TypeReference type)
        {
            TypeReference other = GetRootReference(type);
            if (other != null)
            {
                // ElementType is used when serializing the Assembly.
                // It should match the actual type (e.g., Boolean for System.Boolean). But because of forwarded types, this is not known at read time, thus having to fix it here.
                var etype = type.GetFieldValue("etype");
                if (etype != (object) 0x0)
                {
                    other.SetFieldValue("etype", etype);
                }

                // when reading forwarded types, we don't know if they are value types, fix that later on
                if (type.IsValueType && !other.IsValueType)
                    other.IsValueType = true;
                return other;
            }
            return null;
        }

        internal T GetOrigTypeScope<T>(TypeDefinition nt) where T : class, IMetadataScope
        {
            return mappings.Where(p => p.Value == nt).Select(p => p.Key.MetadataScope).FirstOrDefault() as T;
        }
    }
}
