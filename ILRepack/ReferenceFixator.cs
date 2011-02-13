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
        private readonly ModuleDefinition target;

        internal ReferenceFixator(ModuleDefinition target)
        {
            this.target = target;
        }

        private TypeReference Fix(TypeReference type)
        {
            return Fix(type, null);
        }

        private ModuleReference Fix(ModuleReference moduleRef)
        {
            ModuleReference nmr = target.ModuleReferences.First(x => x.Name == moduleRef.Name);
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

        private HashSet<GenericParameter> fixedGenericParameters = new HashSet<GenericParameter>();
        private TypeReference Fix(TypeReference type, IGenericParameterProvider context)
        {
            if (type == null || type.IsDefinition)
                return type;

            if (type.IsGenericParameter)
            {
                var genPar = (GenericParameter) type;
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
                var t2 = target.GetType(type.FullName);
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
            for(int i = 0; i < args.Count; i++)
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
            foreach(SecurityDeclaration sd in securitydeclarations)
            {
                foreach (SecurityAttribute sa in sd.SecurityAttributes)
                {
                    sa.AttributeType = Fix(sa.AttributeType, context);
                    FixReferences(sa.Fields, context);
                    FixReferences(sa.Properties, context);
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
            if (meth.IsVirtual)
            {
                // Ensure we do not reduce access if the overridden method is in the same assembly
                // (reducing access from 'public' to 'internal' works with different assemblies)
                MethodDefinition baseMeth = GetOuterOverridenMethodDef(meth, context);
                if ((baseMeth != null) && (baseMeth != meth))
                {
                    MethodAttributes baseAttrs = baseMeth.Attributes;
                    MethodAttributes methAttrs = meth.Attributes;
                    MethodAttributes baseAccess = baseAttrs & MethodAttributes.MemberAccessMask;
                    MethodAttributes methAccess = methAttrs & MethodAttributes.MemberAccessMask;
                    if (baseAccess != methAccess)
                    {
                        MethodAttributes newMethAttrs = methAttrs & ~MethodAttributes.MemberAccessMask;
                        newMethAttrs |= baseAccess;
                        meth.Attributes = newMethAttrs;
                    }
                }
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
            foreach(CustomAttribute attribute in attributes)
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
            // if declaring type is in our new merged module, return the definition
            TypeReference declaringType = Fix(method.DeclaringType, context);
            if (method.IsGenericInstance)
            {
                return Fix((GenericInstanceMethod)method, context);
            }
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
                TypeReference fmt = ((OptionalModifierType)type).ModifierType;
                return new OptionalModifierType(fmt, fet);
            }
            if (type is RequiredModifierType)
            {
                TypeReference fmt = ((RequiredModifierType)type).ModifierType;
                return new RequiredModifierType(fmt, fet);
            }
            if (type is GenericInstanceType)
            {
                var instance = (GenericInstanceType)type;
                var element_type = Fix(instance.ElementType, context);
                var imported_instance = new GenericInstanceType(element_type);

                var arguments = instance.GenericArguments;
                var imported_arguments = imported_instance.GenericArguments;

                for (int i = 0; i < arguments.Count; i++)
                    imported_arguments.Add(Fix(arguments[i], context));

                return imported_instance;
            }
            throw new InvalidOperationException();
        }

        /// <summary>
        /// Returns the base-most MethodDefinition (for implicitly or explicitly overridden methods) in this assembly.
        /// (Meaning overridden methods in referenced assemblies do not count, therefore it returns a Definition, not a Reference.)
        /// </summary>
        public MethodDefinition GetOuterOverridenMethodDef(MethodDefinition meth, IGenericParameterProvider context)
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
                            return GetOuterOverridenMethodDef((MethodDefinition)fixedOv, context);
                    }
                }
            }

            // no explicit overrides found, check implicit overrides
            TypeReference baseType = meth.DeclaringType.BaseType;
            if ((baseType != null) && (baseType.Module == meth.Module))
            {
                TypeDefinition baseTypeDef = baseType.Resolve();
                if (baseTypeDef != null)
                {
                    if (baseTypeDef.Module == meth.Module)
                    {
                        MethodDefinition baseMeth = ReflectionHelper.FindMethodDefinitionInType(baseTypeDef, meth);
                        if (baseMeth != null)
                        {
                            if (baseMeth.IsVirtual)
                                return GetOuterOverridenMethodDef(baseMeth, context);
                        }
                    }
                }
            }
            // no overridden method found, return the original method
            return meth;
        }
    }
}
