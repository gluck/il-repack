using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace ILRepack
{
    // worse class name ever
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

        private void FixReferences(FieldReference field, IGenericParameterProvider context)
        {
            field.DeclaringType = Fix(field.DeclaringType, context);
            field.FieldType = Fix(field.FieldType, context);
        }

        private TypeReference Fix(TypeReference type, IGenericParameterProvider context)
        {
            if (type == null || type.IsDefinition || type.IsGenericParameter)
                return type;

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
                    return ((TypeDefinition)type.DeclaringType).NestedTypes.Where(x => x.FullName == type.FullName).First();
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

            // FixReferences(type.SecurityDeclarations);
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

        private void FixReferences(MethodDefinition meth, IGenericParameterProvider context)
        {
            // FixReferences(meth.GenericParameters, meth);
            FixReferences(meth.Parameters, meth);
            FixReferences(meth.Overrides, meth);
            // FixReferences(meth.SecurityDeclarations);
            FixReferences(meth.CustomAttributes, meth);

            meth.ReturnType = Fix(meth.ReturnType, meth);
            if (meth.HasBody)
                FixReferences(meth.Body, meth);
            if (meth.IsAssembly)
            {
                // Ensure we do not reduce access if the overridden method is in the same assembly
                // (reducing access from 'public' to 'internal' works with different assemblies)
                if (ReflectionHelper.MethodOverridesInternalPublic(meth))
                {
                    meth.IsPublic = true;
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
                    FixReferences((FieldReference)instr.Operand, context);
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
                        FixReferences((FieldReference)instr.Operand, context);
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
            for (int i = 0; i < attributes.Count; i++)
            {
                attributes[i].Constructor = Fix(attributes[i].Constructor, context);
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

        private void FixReferences(EventDefinition definition, IGenericParameterProvider context)
        {
            definition.EventType = Fix(definition.EventType, context);
            FixReferences(definition.CustomAttributes, context);
        }

        private void FixReferences(PropertyDefinition definition, IGenericParameterProvider context)
        {
            definition.PropertyType = Fix(definition.PropertyType, context);
            FixReferences(definition.CustomAttributes, context);
        }

        private void FixReferences(ParameterDefinition definition, IGenericParameterProvider context)
        {
            definition.ParameterType = Fix(definition.ParameterType, context);
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
            if (method.IsGenericInstance)
            {
                return Fix((GenericInstanceMethod)method, context);
            }
            method.DeclaringType = Fix(method.DeclaringType, context);
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
    }
}
