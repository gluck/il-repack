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

            TypeDefinition t = type;
            while (t != null)
            {
                IEnumerable<MethodDefinition> realMds = t.Methods.Where(x => x.Name == method.Name && AreSame(x.Parameters, method.Parameters) && AreSame(x.ReturnType, method.ReturnType));
                if (realMds.Count() == 1)
                {
                    MethodDefinition realMd = realMds.First();
                    return realMd;
                }
                if (realMds.Count() > 1)
                {
                    throw new NotSupportedException();
                }
                if ((t.BaseType != null) && (t.BaseType.IsDefinition))
                {
                    // IsDefinition: BaseType is in the same assembly
                    t = (TypeDefinition)t.BaseType;
                }
                else
                {
                    // no base type or in other assembly
                    return null;
                }
            }
            return null;
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
}
