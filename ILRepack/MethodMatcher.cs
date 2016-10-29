using Mono.Cecil;

namespace ILRepacking
{
    /// <summary>
    /// Resolution of overrides and implements of methods.
    /// Copied and modified from http://markmail.org/message/srpyljbjtaskoahk
    /// Which was copied and modified from Mono's Mono.Linker.Steps.TypeMapStep
    /// </summary>
    internal class MethodMatcher
    {
        public static MethodDefinition MapVirtualMethodToDeepestBase(MethodDefinition method)
        {
            MethodDefinition baseMethod = null;
            var candidate = GetBaseMethodInTypeHierarchy(method);
            while (candidate != null)
            {
                baseMethod = candidate;
                candidate = GetBaseMethodInTypeHierarchy(baseMethod);
            }
            return baseMethod;
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