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

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ILRepacking
{
    internal class ReferenceFixator
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private string targetAssemblyPublicKeyBlobString;
        private readonly HashSet<GenericParameter> fixedGenericParameters = new HashSet<GenericParameter>();
        private bool renameIkvmAttributeReference;

        public ReferenceFixator(ILogger logger, IRepackContext repackContext)
        {
            _repackContext = repackContext;
            _logger = logger;
        }

        private ModuleReference Fix(ModuleReference moduleRef)
        {
            ModuleReference nmr = _repackContext.TargetAssemblyMainModule.ModuleReferences.FirstOrDefault(x => x.Name == moduleRef.Name);
            if (nmr == null)
                throw new NullReferenceException("referenced module not found: \"" + moduleRef.Name + "\".");
            return nmr;
        }

        private FieldReference Fix(FieldReference field)
        {
            field.DeclaringType = Fix(field.DeclaringType);
            if (field.DeclaringType.IsDefinition && !field.IsDefinition)
            {
                FieldDefinition def = ((TypeDefinition)field.DeclaringType).Fields.FirstOrDefault(x => x.Name == field.Name);
                if (def == null)
                    throw new NullReferenceException("Field \"" + field + "\" not found in type \"" + field.DeclaringType + "\".");
                return def;
            }
            field.FieldType = Fix(field.FieldType);
            return field;
        }

        private TypeReference Fix(TypeReference type)
        {
            if (type == null || type.IsDefinition)
                return type;

            if (type.IsGenericParameter)
            {
                var genPar = (GenericParameter)type;
                if (!fixedGenericParameters.Contains(genPar))
                {
                    fixedGenericParameters.Add(genPar);
                    FixReferences(genPar.Constraints);
                    FixReferences(genPar.CustomAttributes);
                }
                return type;
            }

            if (type is TypeSpecification)
                return Fix((TypeSpecification)type);

            type = _repackContext.GetExportedTypeFromTypeRef(type);

            var t2 = _repackContext.GetMergedTypeFromTypeRef(type);
            if (t2 != null)
                return t2;

            if (type.IsNested)
                type.DeclaringType = Fix(type.DeclaringType);

            if (type.DeclaringType is TypeDefinition)
                return ((TypeDefinition)type.DeclaringType).NestedTypes.FirstOrDefault(x => x.FullName == type.FullName);

            return type;
        }

        internal void FixMethodVisibility(TypeDefinition type)
        {
            foreach (TypeDefinition nested in type.NestedTypes)
                FixMethodVisibility(nested);

            // Ensure we do not reduce access if the overridden method is in the same assembly
            // (reducing access from 'public' to 'internal' works with different assemblies)
            // this causes peverify issues with IKVM assemblies where java is more flexible such as: (A and B are classes, C interface, B extends A, C)
            // - protected virtual A::foo() {}
            // - C::foo();
            // - public override B::foo() {}
            // Peverify doesn't complain about this, but it complains if we alter the later to protected

            foreach (MethodDefinition meth in type.Methods.Where(meth => meth.IsVirtual && !meth.IsNewSlot))
                FixOverridenMethodDef(meth);
        }

        internal void FixReferences(TypeDefinition type)
        {
            FixReferences(type.GenericParameters);

            type.BaseType = Fix(type.BaseType);

            // interfaces before methods, because methods will have to go through them
            FixReferences(type.Interfaces);

            // nested types first
            foreach (TypeDefinition nested in type.NestedTypes)
                FixReferences(nested);
            foreach (FieldDefinition field in type.Fields)
                FixReferences(field);
            foreach (MethodDefinition meth in type.Methods)
                FixReferences(meth);
            foreach (EventDefinition evt in type.Events)
                FixReferences(evt);
            foreach (PropertyDefinition prop in type.Properties)
                FixReferences(prop);

            FixReferences(type.SecurityDeclarations);
            FixReferences(type.CustomAttributes);
        }

        // replaces TypeRef by TypeDef

        private void FixReferences(FieldDefinition field)
        {
            field.FieldType = Fix(field.FieldType);
            FixReferences(field.CustomAttributes);
        }

        private void FixReferences(VariableDefinition var)
        {
            var.VariableType = Fix(var.VariableType);
        }

        private CustomAttributeArgument Fix(CustomAttributeArgument arg)
        {
            CustomAttributeArgument ret = new CustomAttributeArgument(Fix(arg.Type), FixCustomAttributeValue(arg.Value));
            return ret;
        }

        private object FixCustomAttributeValue(object obj)
        {
            if (obj is TypeReference)
                return Fix((TypeReference)obj);
            if (obj is CustomAttributeArgument)
                return Fix((CustomAttributeArgument)obj);
            if (obj is CustomAttributeArgument[])
                return Array.ConvertAll((CustomAttributeArgument[])obj, a => Fix(a));
            if (renameIkvmAttributeReference && obj is string)
                return _repackContext.FixReferenceInIkvmAttribute((string)obj);
            return obj;
        }

        private CustomAttributeNamedArgument Fix(CustomAttributeNamedArgument namedArg)
        {
            CustomAttributeNamedArgument ret = new CustomAttributeNamedArgument(namedArg.Name, Fix(namedArg.Argument));
            return ret;
        }

        private void FixReferences(Collection<CustomAttributeArgument> args)
        {
            for (int i = 0; i < args.Count; i++)
            {
                args[i] = Fix(args[i]);
            }
        }

        private void FixReferences(Collection<CustomAttributeNamedArgument> namedArgs)
        {
            for (int i = 0; i < namedArgs.Count; i++)
            {
                namedArgs[i] = Fix(namedArgs[i]);
            }
        }

        internal void FixReferences(Collection<SecurityDeclaration> securitydeclarations)
        {
            if (securitydeclarations.Count > 0)
            {
                foreach (SecurityDeclaration sd in securitydeclarations)
                {
                    foreach (SecurityAttribute sa in sd.SecurityAttributes)
                    {
                        sa.AttributeType = Fix(sa.AttributeType);
                        FixReferences(sa.Fields);
                        if (sa.HasFields)
                            throw new NotSupportedException();
                        FixReferences(sa.Properties);
                        if (sa.HasProperties)
                        {
                            foreach (var prop in sa.Properties.ToArray())
                            {
                                if (prop.Name == "PublicKeyBlob")
                                {
                                    if (_repackContext.TargetAssemblyDefinition.Name.HasPublicKey)
                                    {
                                        if (targetAssemblyPublicKeyBlobString == null)
                                            foreach (byte b in _repackContext.TargetAssemblyDefinition.Name.PublicKey)
                                                targetAssemblyPublicKeyBlobString += b.ToString("X").PadLeft(2, '0');
                                        if (prop.Argument.Type.FullName != "System.String")
                                            throw new NotSupportedException("Invalid type of argument, expected string");
                                        CustomAttributeNamedArgument newProp = new CustomAttributeNamedArgument(prop.Name,
                                            new CustomAttributeArgument(prop.Argument.Type, targetAssemblyPublicKeyBlobString));
                                        sa.Properties.Remove(prop);
                                        sa.Properties.Add(newProp);
                                    }
                                    else
                                    {
                                        _logger.Warn("SecurityPermission with PublicKeyBlob found but target has no strong name!");
                                    }
                                }
                            }
                        }
                    }
                }
                if ((_repackContext.TargetAssemblyMainModule.Runtime == TargetRuntime.Net_1_0) || (_repackContext.TargetAssemblyMainModule.Runtime == TargetRuntime.Net_1_1))
                {
                    SecurityDeclaration[] sdArray = securitydeclarations.ToArray();
                    securitydeclarations.Clear();
                    foreach (SecurityDeclaration sd in sdArray)
                        securitydeclarations.Add(PermissionsetHelper.Permission2XmlSet(sd, _repackContext.TargetAssemblyMainModule));
                }
            }
        }

        private void FixReferences(MethodDefinition meth)
        {
            if (meth.HasPInvokeInfo && meth.PInvokeInfo != null)
            {
                meth.PInvokeInfo.Module = Fix(meth.PInvokeInfo.Module);
            }
            FixReferences(meth.GenericParameters);
            FixReferences(meth.Parameters);
            FixReferences(meth.Overrides);
            FixReferences(meth.SecurityDeclarations);
            FixReferences(meth.CustomAttributes);

            meth.ReturnType = Fix(meth.ReturnType);
            FixReferences(meth.MethodReturnType.CustomAttributes);
            if (meth.HasBody)
                FixReferences(meth.Body);
        }

        private void FixReferences(MethodBody body)
        {
            foreach (VariableDefinition var in body.Variables)
                FixReferences(var);

            foreach (Instruction instr in body.Instructions)
            {
                FixReferences(instr);
            }
            foreach (ExceptionHandler eh in body.ExceptionHandlers)
            {
                switch (eh.HandlerType)
                {
                    case ExceptionHandlerType.Catch:
                        eh.CatchType = Fix(eh.CatchType);
                        break;
                }
            }
        }

        private void FixReferences(Instruction instr)
        {
            if (instr.OpCode.Code == Code.Calli)
            {
                var call_site = (Mono.Cecil.CallSite)instr.Operand;
                call_site.ReturnType = Fix(call_site.ReturnType);
            }
            else switch (instr.OpCode.OperandType)
                {
                    case OperandType.InlineField:
                        instr.Operand = Fix((FieldReference)instr.Operand);
                        break;
                    case OperandType.InlineMethod:
                        instr.Operand = Fix((MethodReference)instr.Operand);
                        break;
                    case OperandType.InlineType:
                        instr.Operand = Fix((TypeReference)instr.Operand);
                        break;
                    case OperandType.InlineTok:
                        if (instr.Operand is TypeReference)
                            instr.Operand = Fix((TypeReference)instr.Operand);
                        else if (instr.Operand is FieldReference)
                            instr.Operand = Fix((FieldReference)instr.Operand);
                        else if (instr.Operand is MethodReference)
                            instr.Operand = Fix((MethodReference)instr.Operand);
                        else
                            throw new InvalidOperationException();
                        break;
                    default:
                        break;
                }
        }

        internal void FixReferences(Collection<ExportedType> exportedTypes)
        {
            // Nothing ?
        }

        internal void FixReferences(Collection<CustomAttribute> attributes)
        {
            foreach (CustomAttribute attribute in attributes)
            {
                renameIkvmAttributeReference = IsAnnotation(attribute.AttributeType.Resolve());
                attribute.Constructor = Fix(attribute.Constructor);
                FixReferences(attribute.ConstructorArguments);
                FixReferences(attribute.Fields);
                FixReferences(attribute.Properties);
            }
        }

        private bool IsAnnotation(TypeDefinition typeAttribute)
        {
            if (typeAttribute == null)
                return false;
            if (typeAttribute.Interfaces.Any(@interface => @interface.FullName == "java.lang.annotation.Annotation"))
                return true;
            return typeAttribute.BaseType != null && IsAnnotation(typeAttribute.BaseType.Resolve());
        }

        private void FixReferences(Collection<TypeReference> refs)
        {
            for (int i = 0; i < refs.Count; i++)
            {
                refs[i] = Fix(refs[i]);
            }
        }

        private void FixReferences(Collection<ParameterDefinition> parameters)
        {
            for (int i = 0; i < parameters.Count; i++)
            {
                FixReferences(parameters[i]);
            }
        }

        private void FixReferences(Collection<GenericParameter> parameters)
        {
            for (int i = 0; i < parameters.Count; i++)
            {
                FixReferences(parameters[i]);
            }
        }

#if DEBUG
        private bool AssertIsDefinitionIfNotNull(MethodDefinition mehod)
        {
            if ((mehod != null) && (!mehod.IsDefinition))
                throw new Exception();
            return true;
        }
#endif

        private void FixReferences(EventDefinition definition)
        {
            definition.EventType = Fix(definition.EventType);
            FixReferences(definition.CustomAttributes);
#if DEBUG
            AssertIsDefinitionIfNotNull(definition.AddMethod);
            AssertIsDefinitionIfNotNull(definition.RemoveMethod);
            definition.OtherMethods.All(x => AssertIsDefinitionIfNotNull(x));
#endif
        }

        private void FixReferences(PropertyDefinition definition)
        {
            definition.PropertyType = Fix(definition.PropertyType);
            FixReferences(definition.CustomAttributes);
#if DEBUG
            AssertIsDefinitionIfNotNull(definition.GetMethod);
            AssertIsDefinitionIfNotNull(definition.SetMethod);
            definition.OtherMethods.All(x => AssertIsDefinitionIfNotNull(x));
#endif
        }

        private void FixReferences(ParameterDefinition definition)
        {
            definition.ParameterType = Fix(definition.ParameterType);
            FixReferences(definition.CustomAttributes);
        }

        private void FixReferences(GenericParameter definition)
        {
            FixReferences(definition.Constraints);
            FixReferences(definition.CustomAttributes);
        }

        private GenericInstanceMethod Fix(GenericInstanceMethod method)
        {
            var element_method = Fix(method.ElementMethod);
            var imported_instance = new GenericInstanceMethod(element_method);

            var arguments = method.GenericArguments;
            var imported_arguments = imported_instance.GenericArguments;

            for (int i = 0; i < arguments.Count; i++)
                imported_arguments.Add(Fix(arguments[i]));

            return imported_instance;
        }

        internal MethodReference Fix(MethodReference method)
        {
            TypeReference declaringType = Fix(method.DeclaringType);
            if (method.IsGenericInstance)
            {
                return Fix((GenericInstanceMethod)method);
            }
            // if declaring type is in our new merged module, return the definition
            if (declaringType.IsDefinition && !method.IsDefinition)
            {
                MethodDefinition def = new ReflectionHelper(_repackContext).FindMethodDefinitionInType((TypeDefinition)declaringType, method);
                if (def != null)
                    return def;
            }
            method.DeclaringType = declaringType;
            method.ReturnType = Fix(method.ReturnType);

            FixReferences(method.Parameters);
            FixReferences(method.GenericParameters);

            if (!method.IsDefinition &&
                !method.DeclaringType.IsGenericInstance &&
                !method.DeclaringType.IsArray &&
                (method.ReturnType.IsDefinition || method.Parameters.Any(x => x.ParameterType.IsDefinition)))
            {
                var culprit = method.ReturnType.IsDefinition
                                     ? method.ReturnType
                                     : method.Parameters.First(x => x.ParameterType.IsDefinition).ParameterType;
                // warn about invalid merge assembly set, as this method is not gonna work fine (peverify would warn as well)
                _logger.Warn("Method reference is used with definition return type / parameter. Indicates a likely invalid set of assemblies, consider one of the following");
                _logger.Warn(" - Remove the assembly defining " + culprit + " from the merge");
                _logger.Warn(" - Add assembly defining " + method + " to the merge");

                // one case where it'll work correctly however (but doesn't seem common):
                // A references B
                // C references A
                // C is merged into B
            }
            return method;
        }

        private void FixReferences(Collection<MethodReference> parameters)
        {
            for (int i = 0; i < parameters.Count; i++)
            {
                parameters[i] = Fix(parameters[i]);
            }
        }

        private TypeSpecification Fix(TypeSpecification type)
        {
            var fet = Fix(type.ElementType);
            if (type is ArrayType)
            {
                var array = (ArrayType)type;
                var imported_array = new ArrayType(fet);
                if (array.IsVector)
                    return imported_array;

                var dimensions = array.Dimensions;
                var imported_dimensions = imported_array.Dimensions;

                imported_dimensions.Clear();

                for (int i = 0; i < dimensions.Count; i++)
                {
                    var dimension = dimensions[i];

                    imported_dimensions.Add(new ArrayDimension(dimension.LowerBound, dimension.UpperBound));
                }

                return imported_array;
            }
            if (type is PointerType)
                return new PointerType(fet);
            if (type is ByReferenceType)
                return new ByReferenceType(fet);
            if (type is PinnedType)
                return new PinnedType(fet);
            if (type is SentinelType)
                return new SentinelType(fet);
            if (type is OptionalModifierType)
            {
                TypeReference fmt = Fix(((OptionalModifierType)type).ModifierType);
                return new OptionalModifierType(fmt, fet);
            }
            if (type is RequiredModifierType)
            {
                TypeReference fmt = Fix(((RequiredModifierType)type).ModifierType);
                return new RequiredModifierType(fmt, fet);
            }
            if (type is GenericInstanceType)
            {
                var instance = (GenericInstanceType)type;
                var imported_instance = new GenericInstanceType(fet);

                var arguments = instance.GenericArguments;
                var imported_arguments = imported_instance.GenericArguments;

                for (int i = 0; i < arguments.Count; i++)
                    imported_arguments.Add(Fix(arguments[i]));

                return imported_instance;
            }
            if (type is FunctionPointerType)
            {
                var funcPtr = (FunctionPointerType)type;
                var imported_instance = new FunctionPointerType()
                {
                    HasThis = funcPtr.HasThis,
                    ExplicitThis = funcPtr.ExplicitThis,
                    CallingConvention = funcPtr.CallingConvention,
                    ReturnType = Fix(funcPtr.ReturnType)
                };
                if (funcPtr.HasParameters)
                {
                    foreach (var pd in funcPtr.Parameters)
                    {
                        imported_instance.Parameters.Add(pd);
                    }
                    FixReferences(imported_instance.Parameters);
                }
                return imported_instance;
            }
            throw new InvalidOperationException();
        }

        void Fix(MethodDefinition @base, MethodDefinition over)
        {
            if ((@base != null) && (@base != over))
            {
                MethodAttributes baseAttrs = @base.Attributes;
                MethodAttributes methAttrs = over.Attributes;
                MethodAttributes baseAccess = baseAttrs & MethodAttributes.MemberAccessMask;
                MethodAttributes methAccess = methAttrs & MethodAttributes.MemberAccessMask;
                if (baseAccess != methAccess)
                {
                    MethodAttributes newMethAttrs = methAttrs & ~MethodAttributes.MemberAccessMask;
                    newMethAttrs |= baseAccess;
                    over.Attributes = newMethAttrs;
                }
            }
        }

        /// <summary>
        /// Returns the base-most MethodDefinition (for implicitly or explicitly overridden methods) in this assembly.
        /// (Meaning overridden methods in referenced assemblies do not count, therefore it returns a Definition, not a Reference.)
        /// </summary>
        public void FixOverridenMethodDef(MethodDefinition meth)
        {
            foreach (var ov in meth.Overrides)
            {
                MethodReference fixedOv = Fix(ov);
                if (fixedOv.IsDefinition)
                {
                    if (fixedOv.Module == meth.Module)
                    {
                        // it's a Definition, and in our module
                        MethodDefinition fixedOvDef = (MethodDefinition)fixedOv;
                        if (fixedOvDef.IsVirtual)
                            Fix((MethodDefinition)fixedOv, meth);
                    }
                }
            }

            // no explicit overrides found, check implicit overrides
            MethodDefinition @base = MethodMatcher.MapVirtualMethodToDeepestBase(meth);
            if (@base != null && @base.IsVirtual)
                Fix(@base, meth);
        }
    }
}
