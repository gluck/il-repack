//
// Copyright (c) 2011 Francois Valdy
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
//
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace ILRepacking
{
    internal static class ReflectionHelper
    {
        internal static MethodDefinition FindMethodDefinitionInType(TypeDefinition type, MethodReference method)
        {
            return type.Methods.Where(
                    x => x.Name == method.Name && 
                         AreSame(x.Parameters, method.Parameters) &&
                         AreSame(x.ReturnType, method.ReturnType)
                  ).FirstOrDefault();
        }

        // nasty copy from MetadataResolver.cs for now
        internal static bool AreSame(Collection<ParameterDefinition> a, Collection<ParameterDefinition> b)
        {
            var count = a.Count;

            if (count != b.Count)
                return false;

            if (count == 0)
                return true;

            for (int i = 0; i < count; i++)
                if (!AreSame(a[i].ParameterType, b[i].ParameterType))
                    return false;

            return true;
        }

        internal static bool AreSame(TypeSpecification a, TypeSpecification b)
        {
            if (!AreSame(a.ElementType, b.ElementType))
                return false;

            if (a.IsGenericInstance)
                return AreSame((GenericInstanceType)a, (GenericInstanceType)b);

            if (a.IsRequiredModifier || a.IsOptionalModifier)
                return AreSame((IModifierType)a, (IModifierType)b);

            if (a.IsArray)
                return AreSame((ArrayType)a, (ArrayType)b);

            return true;
        }

        internal static bool AreSame(ArrayType a, ArrayType b)
        {
            if (a.Rank != b.Rank)
                return false;

            // TODO: dimensions

            return true;
        }

        internal static bool AreSame(IModifierType a, IModifierType b)
        {
            return AreSame(a.ModifierType, b.ModifierType);
        }

        internal static bool AreSame(GenericInstanceType a, GenericInstanceType b)
        {
            if (!a.HasGenericArguments)
                return !b.HasGenericArguments;

            if (!b.HasGenericArguments)
                return false;

            if (a.GenericArguments.Count != b.GenericArguments.Count)
                return false;

            for (int i = 0; i < a.GenericArguments.Count; i++)
                if (!AreSame(a.GenericArguments[i], b.GenericArguments[i]))
                    return false;

            return true;
        }

        internal static bool AreSame(GenericParameter a, GenericParameter b)
        {
            return a.Position == b.Position;
        }

        internal static bool AreSame(TypeReference a, TypeReference b)
        {
            if (a.MetadataType != b.MetadataType)
                return false;

            if (a.IsGenericParameter)
                return AreSame((GenericParameter)a, (GenericParameter)b);

            if (a is TypeSpecification)
                return AreSame((TypeSpecification)a, (TypeSpecification)b);

            return a.FullName == b.FullName;
        }

    }



    /// <summary>
    /// Resolution of overrides and implements of methods.
    /// Copied and modified from http://markmail.org/message/srpyljbjtaskoahk
    /// Which was copied and modified from Mono's Mono.Linker.Steps.TypeMapStep
    /// </summary>
    public class MethodMatcher
    {
        public static MethodDefinition MapVirtualMethod(MethodDefinition method)
        {
            return GetBaseMethodInTypeHierarchy(method);
        }

        static MethodDefinition GetBaseMethodInTypeHierarchy(MethodDefinition method)
        {
            TypeDefinition @base = GetBaseType(method.DeclaringType);
            while (@base != null)
            {
                MethodDefinition baseMethod = TryMatchMethod(@base, method);
                if (baseMethod != null)
                    return baseMethod;

                @base = GetBaseType(@base);
            }

            return null;
        }

        static MethodDefinition TryMatchMethod(TypeDefinition type, MethodDefinition method)
        {
            if (!type.HasMethods)
                return null;

            foreach (MethodDefinition candidate in type.Methods)
                if (MethodMatch(candidate, method))
                    return candidate;

            return null;
        }

        static bool MethodMatch(MethodDefinition candidate, MethodDefinition method)
        {
            if (!candidate.IsVirtual)
                return false;

            if (candidate.Name != method.Name)
                return false;

            if (!TypeMatch(candidate.ReturnType, method.ReturnType))
                return false;

            if (candidate.Parameters.Count != method.Parameters.Count)
                return false;

            for (int i = 0; i < candidate.Parameters.Count; i++)
                if (!TypeMatch(candidate.Parameters[i].ParameterType, method.Parameters[i].ParameterType))
                    return false;

            return true;
        }
        
        static bool TypeMatch(IModifierType a, IModifierType b)
        {
            if (!TypeMatch(a.ModifierType, b.ModifierType))
                return false;

            return TypeMatch(a.ElementType, b.ElementType);
        }
        

        static bool TypeMatch(TypeSpecification a, TypeSpecification b)
        {
            if (a.IsGenericInstance)
                return TypeMatch((GenericInstanceType)a, (GenericInstanceType)b);

            if (a.IsRequiredModifier || a.IsOptionalModifier)
                return TypeMatch((IModifierType)a, (IModifierType)b);

            return TypeMatch(a.ElementType, b.ElementType);
        }

        static bool TypeMatch(GenericInstanceType a, GenericInstanceType b)
        {
            if (!TypeMatch(a.ElementType, b.ElementType))
                return false;

            if (a.GenericArguments.Count != b.GenericArguments.Count)
                return false;

            if (a.GenericArguments.Count == 0)
                return true;

            for (int i = 0; i < a.GenericArguments.Count; i++)
                if (!TypeMatch(a.GenericArguments[i], b.GenericArguments[i]))
                    return false;

            return true;
        }

        static bool TypeMatch(TypeReference a, TypeReference b)
        {
            if (a is GenericParameter)
                return true; // not exact, but a guess is enough for us

            if (a is TypeSpecification || b is TypeSpecification)
            {
                if (a.GetType() != b.GetType())
                    return false;

                return TypeMatch((TypeSpecification)a, (TypeSpecification)b);
            }

            return a.FullName == b.FullName;
        }

        static TypeDefinition GetBaseType(TypeDefinition type)
        {
            if (type == null || type.BaseType == null)
                return null;
            // Class<String> -> Class<T>
            var type2 = type.BaseType.GetElementType();
            return type2 as TypeDefinition;
        }
    }
}
