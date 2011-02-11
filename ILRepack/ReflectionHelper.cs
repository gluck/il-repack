using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace ILRepack
{
    internal class ReflectionHelper
    {
        public static bool MethodParametersSeemEqual(Collection<ParameterDefinition> a, Collection<ParameterDefinition> b)
        {
            if (a.Count == b.Count)
            {
                for (int i = 0; i < a.Count; i++)
                {
                    ParameterDefinition pa = a[i];
                    ParameterDefinition pb = b[i];
                    if (pa.ParameterType.FullName != pb.ParameterType.FullName)
                    {
                        return false;
                    }
                    if (pa.ParameterType.IsArray != pb.ParameterType.IsArray)
                    {
                        return false;
                    }
                    if (pa.ParameterType.IsArray)
                    {
                        ArrayType atA = pa.ParameterType as ArrayType;
                        ArrayType atB = pb.ParameterType as ArrayType;
                        if ((atA == null) || (atB == null))
                        {
                            return false;
                        }
                        if (atA.Rank != atB.Rank)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            return false;
        }

        public static MethodDefinition FindMethodDefinitionInType(TypeDefinition type, string methodName, Collection<ParameterDefinition> methodParameters)
        {
            TypeDefinition t = type;
            while (t != null)
            {
                IEnumerable<MethodDefinition> realMds = t.Methods.Where(x => x.Name == methodName && ReflectionHelper.MethodParametersSeemEqual(x.Parameters, methodParameters));
                if (realMds.Count() == 1)
                {
                    MethodDefinition realMd = realMds.First();
                    return realMd;
                }
                else if (realMds.Count() > 1)
                {
                    throw new NotSupportedException();
                }
                else if ((t.BaseType != null) && (t.BaseType.IsDefinition))
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
    }
}
