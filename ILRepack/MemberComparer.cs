using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace ILRepacking
{
    public static class MemberComparer
    {
        internal static bool ParamsMatch(Collection<ParameterDefinition> a, Collection<ParameterDefinition> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
                if (a[i].Attributes != b[i].Attributes || !AttrsMatch(a[i].CustomAttributes, b[i].CustomAttributes) ||
                        !TypesMatch(a[i].ParameterType, b[i].ParameterType))
                    return false;

            return true;
        }

        internal static bool AttrArgsMatch(Collection<CustomAttributeArgument> a, Collection<CustomAttributeArgument> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
                if (!TypesMatch(a[i].Type, b[i].Type) || a[i].Value != b[i].Value) // constants only, this should be OK
                    return false;

            return true;
        }

        internal static bool AttrsMatch(Collection<CustomAttribute> a, Collection<CustomAttribute> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
                if (!TypesMatch(a[i].AttributeType, b[i].AttributeType) || a[i].Constructor.FullName != b[i].Constructor.FullName /* meh */ ||
                        !AttrArgsMatch(a[i].ConstructorArguments, b[i].ConstructorArguments))
                    return false;

            return true;
        }

        internal static bool GenericParamsMatch(Collection<GenericParameter> a, Collection<GenericParameter> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
                if (a[i].Attributes != b[i].Attributes || !GenericParamsMatch(a[i].GenericParameters, b[i].GenericParameters) ||
                        /*AttrsMatch(a[i].CustomAttributes, b[i].CustomAttributes)*/ a[i].Type != b[i].Type)
                    return false;

            return true;
        }

        internal static bool TypesMatch(TypeReference a, TypeReference b)
        {
            return a == null && b == null || (a.FullName == b.FullName && GenericParamsMatch(a.GenericParameters, b.GenericParameters) && TypesMatch(a.DeclaringType, b.DeclaringType)
                && a.IsArray == b.IsArray && a.IsByReference == b.IsByReference && a.IsDefinition == b.IsDefinition && a.IsFunctionPointer == b.IsFunctionPointer &&
                a.IsGenericInstance == b.IsGenericInstance && a.IsGenericParameter == b.IsGenericParameter && a.IsOptionalModifier == b.IsOptionalModifier &&
                a.IsPinned == b.IsPinned && a.IsPointer == b.IsPointer && a.IsPrimitive == b.IsPrimitive && a.IsRequiredModifier == b.IsRequiredModifier &&
                a.IsSentinel == b.IsSentinel && a.IsValueType == b.IsValueType);
        }

        internal static bool SecurityAttrsMatch(Collection<SecurityAttribute> a, Collection<SecurityAttribute> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
                if (!TypesMatch(a[i].AttributeType, b[i].AttributeType))
                    return false;

            return true;
        }

        internal static bool SecurityDeclarationsMatch(Collection<SecurityDeclaration> a, Collection<SecurityDeclaration> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
                if (a[i].Action != b[i].Action || !SecurityAttrsMatch(a[i].SecurityAttributes, b[i].SecurityAttributes))
                    return false;

            return true;
        }

        internal static bool PInvokeInfosMatch(PInvokeInfo a, PInvokeInfo b)
        {
            return a == null && b == null || (a.Attributes == b.Attributes && a.EntryPoint == b.EntryPoint && a.SupportsLastError == b.SupportsLastError);
        }

        internal static bool MethodsMatch(MethodDefinition a, MethodDefinition b)
        {
            // this can be changed, but I left some bits out so both methods shouldn't be completely identical
            return a.Name == b.Name && ParamsMatch(a.Parameters, b.Parameters) &&
                (a.Attributes ^ (a.Attributes & MethodAttributes.HideBySig))
                    == (b.Attributes ^ (b.Attributes & MethodAttributes.HideBySig)) &&
                //a.CallingConvention == b.CallingConvention && //AttrsMatch(a.CustomAttributes, b.CustomAttributes) &&
                a.FullName == b.FullName && GenericParamsMatch(a.GenericParameters, b.GenericParameters) &&
                (a.ImplAttributes ^ (a.ImplAttributes & MethodImplAttributes.ForwardRef))
                    == (b.ImplAttributes ^ (b.ImplAttributes & MethodImplAttributes.ForwardRef)) && // see CloneTo(MethodDefinition, TypeDefinition, bool)
                a.IsConstructor == b.IsConstructor && a.SemanticsAttributes == b.SemanticsAttributes &&
                TypesMatch(a.ReturnType, b.ReturnType) //&& //SecurityDeclarationsMatch(a.SecurityDeclarations, b.SecurityDeclarations) &&
                ;//PInvokeInfoMatches(a.PInvokeInfo, b.PInvokeInfo);
        }
    }
}
