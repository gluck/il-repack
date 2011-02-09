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
			IEnumerable<MethodDefinition> realMds = type.Methods.Where(x => x.Name == methodName && ReflectionHelper.MethodParametersSeemEqual(x.Parameters, methodParameters));
			if (realMds != null)
			{
				if (realMds.Count() == 1)
				{
					MethodDefinition realMd = realMds.First();
					return realMd;
				}
				else if (realMds.Count() > 1)
				{
					throw new NotSupportedException();
				}
			}
			return null;
		}

		/// <summary>
		/// Returns true if explicit or implicit overrides of this function are in the same assembly and are public.
		/// </summary>
		public static bool MethodOverridesInternalPublic(MethodDefinition meth)
		{
			foreach (var ov in meth.Overrides)
			{
				if (ov.IsDefinition)
				{
					// are they in the same module
					if (ov.Module == meth.Module)
					{
						MethodDefinition def = (MethodDefinition)ov;
						if (def.IsPublic)
						{
							Console.WriteLine("Explicit override is public: " + def);
							return true;
						}
					}
					else
					{
						int i = 0;
						i++;
					}
				}
			}
			// no matching explicit overrides found, check implicit overrides
			TypeReference baseType = meth.DeclaringType.BaseType;
			// iterate over all base types in the same module
			while((baseType != null) && (baseType.Module == meth.Module))
			{
				TypeDefinition baseTypeDef = baseType.Resolve();
				if (baseTypeDef == null)
				{
					throw new NotImplementedException(); // TODO: just break;?
				}
				if (baseTypeDef.Module != meth.Module)
				{
					break;
				}
				MethodDefinition baseMeth = FindMethodDefinitionInType(baseTypeDef, meth.Name, meth.Parameters);
				if (baseMeth != null)
				{
					if (baseMeth.IsPublic)
					{
						Console.WriteLine("Explicit override is public: " + baseMeth);
						return true;
					}
				}
                baseType = baseTypeDef.BaseType;
			}
			return false;
		}
	}
}
