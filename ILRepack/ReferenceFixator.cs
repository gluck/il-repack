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
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace ILRepacking
{
    internal class ReferenceFixator
    {
        private readonly ILRepack repack;
        private string targetAssemblyPublicKeyBlobString;
        private HashSet<GenericParameter> fixedGenericParameters = new HashSet<GenericParameter>();
        private DuplicateHandler duplicateHandler;

        public ReferenceFixator(ILRepack iLRepack, DuplicateHandler duplicateHandler)
        {
            this.repack = iLRepack;
            this.duplicateHandler = duplicateHandler;
        }

        private TypeReference Fix(TypeReference type)
        {
            return Fix(type, null);
        }

        private ModuleReference Fix(ModuleReference moduleRef)
        {
            ModuleReference nmr = repack.TargetAssemblyMainModule.ModuleReferences.First(x => x.Name == moduleRef.Name);
            if (nmr == null)
                throw new NullReferenceException("referenced module not found: \"" + moduleRef.Name + "\".");
            return nmr;
        }

        private FieldReference Fix(FieldReference field, IGenericParameterProvider context)
        {
            field.DeclaringType = Fix(field.DeclaringType, context);
            if (field.DeclaringType.IsDefinition && !field.IsDefinition)
            {
                FieldDefinition def = ((TypeDefinition)field.DeclaringType).Fields.First(x => x.Name == field.Name);
                if (def == null)
                    throw new NullReferenceException("Field \"" + field + "\" not found in type \"" + field.DeclaringType + "\".");
                return def;
            }
            field.FieldType = Fix(field.FieldType, context);
            return field;
        }

        private TypeReference Fix(TypeReference type, IGenericParameterProvider context)
        {
            if (type == null || type.IsDefinition)
                return type;

            if (type.IsGenericParameter)
            {
                var genPar = (GenericParameter)type;
                if (!fixedGenericParameters.Contains(genPar))
                {
                    fixedGenericParameters.Add(genPar);
                    FixReferences(genPar.Constraints, context);
                    FixReferences(genPar.CustomAttributes, context);
                }
                return type;
            }

            if (type is TypeSpecification)
            {
                return Fix((TypeSpecification)type, context);
            }
            if (type.IsNested)
            {
                type.DeclaringType = Fix(type.DeclaringType, context);
                // might need to do more
                if (type.DeclaringType is TypeDefinition)
                {
                    return ((TypeDefinition)type.DeclaringType).NestedTypes.First(x => x.FullName == type.FullName);
                }
            }
            else
            {
                type = duplicateHandler.Rename(type);
                return FixImpl(type);
            }
            return type;
        }

        private TypeReference FixImpl(TypeReference type)
        {
            // don't fix reference to an unmerged type (even if a merged one exists with same name)
            if (repack.IsMerged(type))
            {
                var t2 = repack.TargetAssemblyMainModule.GetType(type.FullName);
                return t2 ?? type;
            }
            return type;
        }

        internal void FixReferences(TypeDefinition type)
        {
            // FixReferences(type.GenericParameters, type);

            type.BaseType = Fix(type.BaseType, type);

            // nested types first
            foreach (TypeDefinition nested in type.NestedTypes)
                FixReferences(nested);
            foreach (FieldDefinition field in type.Fields)
                FixReferences(field, type);
            foreach (MethodDefinition meth in type.Methods)
                FixReferences(meth, type);
            foreach (EventDefinition evt in type.Events)
                FixReferences(evt, type);
            foreach (PropertyDefinition prop in type.Properties)
                FixReferences(prop, type);

            FixReferences(type.SecurityDeclarations, type);
            FixReferences(type.Interfaces, type);
            FixReferences(type.CustomAttributes, type);
        }

        // replaces TypeRef by TypeDef

        private void FixReferences(FieldDefinition field, IGenericParameterProvider context)
        {
            field.FieldType = Fix(field.FieldType, context);
            FixReferences(field.CustomAttributes, context);
        }

        private void FixReferences(VariableDefinition var, IGenericParameterProvider context)
        {
            var.VariableType = Fix(var.VariableType, context);
        }

        private CustomAttributeArgument Fix(CustomAttributeArgument arg, IGenericParameterProvider context)
        {
            CustomAttributeArgument ret = new CustomAttributeArgument(Fix(arg.Type, context), arg.Value);
            return ret;
        }

        private CustomAttributeNamedArgument Fix(CustomAttributeNamedArgument namedArg, IGenericParameterProvider context)
        {
            CustomAttributeNamedArgument ret = new CustomAttributeNamedArgument(namedArg.Name, Fix(namedArg.Argument, context));
            return ret;
        }

        private void FixReferences(Collection<CustomAttributeArgument> args, IGenericParameterProvider context)
        {
            for (int i = 0; i < args.Count; i++)
            {
                args[i] = Fix(args[i], context);
            }
        }

        private void FixReferences(Collection<CustomAttributeNamedArgument> namedArgs, IGenericParameterProvider context)
        {
            for (int i = 0; i < namedArgs.Count; i++)
            {
                namedArgs[i] = Fix(namedArgs[i], context);
            }
        }

        internal void FixReferences(Collection<SecurityDeclaration> securitydeclarations, IGenericParameterProvider context)
        {
            if (securitydeclarations.Count > 0)
            {
                foreach (SecurityDeclaration sd in securitydeclarations)
                {
                    foreach (SecurityAttribute sa in sd.SecurityAttributes)
                    {
                        sa.AttributeType = Fix(sa.AttributeType, context);
                        FixReferences(sa.Fields, context);
                        if (sa.HasFields)
                            throw new NotSupportedException();
                        FixReferences(sa.Properties, context);
                        if (sa.HasProperties)
                        {
                            foreach (var prop in sa.Properties.ToArray())
                            {
                                if (prop.Name == "PublicKeyBlob")
                                {
                                    if (repack.TargetAssemblyDefinition.Name.HasPublicKey)
                                    {
                                        if (targetAssemblyPublicKeyBlobString == null)
                                            foreach (byte b in repack.TargetAssemblyDefinition.Name.PublicKey)
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
                                        repack.WARN("SecurityPermission with PublicKeyBlob found but target has no strong name!");
                                    }
                                }
                            }
                        }
                    }
                }
                if ((repack.TargetAssemblyMainModule.Runtime == TargetRuntime.Net_1_0) || (repack.TargetAssemblyMainModule.Runtime == TargetRuntime.Net_1_1))
                {
                    SecurityDeclaration[] sdArray = securitydeclarations.ToArray();
                    securitydeclarations.Clear();
                    foreach (SecurityDeclaration sd in sdArray)
                        securitydeclarations.Add(PermissionsetHelper.Permission2XmlSet(sd, repack.TargetAssemblyMainModule));
                }
            }
        }

        private void FixReferences(MethodDefinition meth, IGenericParameterProvider context)
        {
            if (meth.HasPInvokeInfo)
            {
                meth.PInvokeInfo.Module = Fix(meth.PInvokeInfo.Module);
            }
            // FixReferences(meth.GenericParameters, meth);
            FixReferences(meth.Parameters, meth);
            FixReferences(meth.Overrides, meth);
            FixReferences(meth.SecurityDeclarations, meth);
            FixReferences(meth.CustomAttributes, meth);

            meth.ReturnType = Fix(meth.ReturnType, meth);
            if (meth.HasBody)
                FixReferences(meth.Body, meth);
            if (meth.IsVirtual && !meth.IsNewSlot)
            {
                // Ensure we do not reduce access if the overridden method is in the same assembly
                // (reducing access from 'public' to 'internal' works with different assemblies)
                FixOverridenMethodDef(meth, context);
                // this causes peverify issues with IKVM assemblies where java is more flexible such as: (A and B are classes, C interface, B extends A, C)
                // - protected virtual A::foo() {}
                // - C::foo();
                // - public override B::foo() {}
                // Peverify doesn't complain about this, but it complains if we alter the later to protected
            }
        }

        private void FixReferences(MethodBody body, IGenericParameterProvider context)
        {
            foreach (VariableDefinition var in body.Variables)
                FixReferences(var, context);

            foreach (Instruction instr in body.Instructions)
            {
                FixReferences(instr, context);
            }
            foreach (ExceptionHandler eh in body.ExceptionHandlers)
            {
                switch (eh.HandlerType)
                {
                    case ExceptionHandlerType.Catch:
                        eh.CatchType = Fix(eh.CatchType, context);
                        break;
                }
            }
        }

        private void FixReferences(Instruction instr, IGenericParameterProvider context)
        {
            switch (instr.OpCode.OperandType)
            {
                case OperandType.InlineField:
                    instr.Operand = Fix((FieldReference)instr.Operand, context);
                    break;
                case OperandType.InlineMethod:
                    instr.Operand = Fix((MethodReference)instr.Operand, context);
                    break;
                case OperandType.InlineType:
                    instr.Operand = Fix((TypeReference)instr.Operand, context);
                    break;
                case OperandType.InlineTok:
                    if (instr.Operand is TypeReference)
                        instr.Operand = Fix((TypeReference)instr.Operand, context);
                    else if (instr.Operand is FieldReference)
                        instr.Operand = Fix((FieldReference)instr.Operand, context);
                    else if (instr.Operand is MethodReference)
                        instr.Operand = Fix((MethodReference)instr.Operand, context);
                    else
                        throw new InvalidOperationException();
                    break;
                default:
                    break;
            }
        }

        internal void FixReferences(Collection<CustomAttribute> attributes, IGenericParameterProvider context)
        {
            foreach (CustomAttribute attribute in attributes)
            {
                attribute.Constructor = Fix(attribute.Constructor, context);
                FixReferences(attribute.ConstructorArguments, context);
                FixReferences(attribute.Fields, context);
                FixReferences(attribute.Properties, context);
            }
        }

        private void FixReferences(Collection<TypeReference> refs, IGenericParameterProvider context)
        {
            for (int i = 0; i < refs.Count; i++)
            {
                refs[i] = Fix(refs[i], context);
            }
        }

        private void FixReferences(Collection<ParameterDefinition> parameters, IGenericParameterProvider context)
        {
            for (int i = 0; i < parameters.Count; i++)
            {
                FixReferences(parameters[i], context);
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

        private void FixReferences(EventDefinition definition, IGenericParameterProvider context)
        {
            definition.EventType = Fix(definition.EventType, context);
            FixReferences(definition.CustomAttributes, context);
#if DEBUG
            AssertIsDefinitionIfNotNull(definition.AddMethod);
            AssertIsDefinitionIfNotNull(definition.RemoveMethod);
            definition.OtherMethods.All(x => AssertIsDefinitionIfNotNull(x));
#endif
        }

        private void FixReferences(PropertyDefinition definition, IGenericParameterProvider context)
        {
            definition.PropertyType = Fix(definition.PropertyType, context);
            FixReferences(definition.CustomAttributes, context);
#if DEBUG
            AssertIsDefinitionIfNotNull(definition.GetMethod);
            AssertIsDefinitionIfNotNull(definition.SetMethod);
            definition.OtherMethods.All(x => AssertIsDefinitionIfNotNull(x));
#endif
        }

        private void FixReferences(ParameterDefinition definition, IGenericParameterProvider context)
        {
            definition.ParameterType = Fix(definition.ParameterType, context);
            FixReferences(definition.CustomAttributes, context);
        }

        private GenericInstanceMethod Fix(GenericInstanceMethod method, IGenericParameterProvider context)
        {
            var element_method = Fix(method.ElementMethod, context);
            var imported_instance = new GenericInstanceMethod(element_method);

            var arguments = method.GenericArguments;
            var imported_arguments = imported_instance.GenericArguments;

            for (int i = 0; i < arguments.Count; i++)
                imported_arguments.Add(Fix(arguments[i], context));

            return imported_instance;
        }

        internal MethodReference Fix(MethodReference method, IGenericParameterProvider context)
        {
            TypeReference declaringType = Fix(method.DeclaringType, context);
            if (method.IsGenericInstance)
            {
                return Fix((GenericInstanceMethod)method, context);
            }
            // if declaring type is in our new merged module, return the definition
            if (declaringType.IsDefinition && !method.IsDefinition)
            {
                MethodDefinition def = ReflectionHelper.FindMethodDefinitionInType((TypeDefinition)declaringType, method);
                if (def != null)
                    // if not found, the method might be outside of the new assembly (virtual call), so go on below
                    return def;
            }
            method.DeclaringType = declaringType;
            method.ReturnType = Fix(method.ReturnType, method);
            // FixReferences(method.GenericParameters, method);
            foreach (var p in method.Parameters)
                FixReferences(p, method);
            if (!method.IsDefinition && !method.DeclaringType.IsGenericInstance && (method.ReturnType.IsDefinition || method.Parameters.Any(x => x.ParameterType.IsDefinition)))
            {
                var culprit = method.ReturnType.IsDefinition
                                     ? method.ReturnType
                                     : method.Parameters.First(x => x.ParameterType.IsDefinition).ParameterType;
                // warn about invalid merge assembly set, as this method is not gonna work fine (peverify would warn as well)
                repack.WARN("Method reference is used with definition return type / parameter. Indicates a likely invalid set of assemblies, consider one of the following");
                repack.WARN(" - Remove the assembly defining " + culprit + " from the merge");
                repack.WARN(" - Add assembly defining " + method + " to the merge");

                // one case where it'll work correctly however (but doesn't seem common):
                // A references B
                // C references A
                // C is merged into B
            }
            return method;
        }

        private void FixReferences(Collection<MethodReference> parameters, IGenericParameterProvider context)
        {
            for (int i = 0; i < parameters.Count; i++)
            {
                parameters[i] = Fix(parameters[i], context);
            }
        }

        private TypeSpecification Fix(TypeSpecification type, IGenericParameterProvider context)
        {
            var fet = Fix(type.ElementType, context);
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
                TypeReference fmt = Fix(((OptionalModifierType)type).ModifierType, context);
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
                    imported_arguments.Add(Fix(arguments[i], context));

                return imported_instance;
            }
            // TODO: what about FunctionPointerType?
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
        public void FixOverridenMethodDef(MethodDefinition meth, IGenericParameterProvider context)
        {
            foreach (var ov in meth.Overrides)
            {
                MethodReference fixedOv = Fix(ov, context);
                if (fixedOv.IsDefinition)
                {
                    if (fixedOv.Module == meth.Module)
                    {
                        // it's a Definition, and in our module
                        MethodDefinition fixedOvDef = (MethodDefinition)fixedOv;
                        if (fixedOvDef.IsVirtual)
                            Fix((MethodDefinition) fixedOv, meth);
                    }
                }
            }

            // no explicit overrides found, check implicit overrides
            new MethodMatcher(x =>
                              {
                                  if (x.IsVirtual)
                                      Fix(x, meth);
                              }).MapVirtualMethod(meth);
        }

        public void FixReferences(Collection<Resource> resources)
        {
            // TODO: check if merged resources contain binary-formatted objects of merged types.            // These don't work any more as the BinaryFormatter tries to load the old assembly.        }
    }
}
