using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace ILRepacking.Steps
{
    internal class PublicTypesFixStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly HashSet<TypeDefinition> _visitedTypes = new HashSet<TypeDefinition>();

        public PublicTypesFixStep(
            ILogger logger,
            IRepackContext repackContext)
        {
            _logger = logger;
            _repackContext = repackContext;
        }

        public void Perform()
        {
            _logger.Verbose("Processing public types tree");

            var publicTypes = _repackContext.TargetAssemblyMainModule.Types.Where(t => t.IsPublic);

            foreach (var type in publicTypes)
            {
                EnsureDependencies(type, new Stack<string>());
            }
        }

        private void EnsureDependencies(TypeDefinition type, Stack<string> callerStack)
        {
            if (type == null) return;

            if (!_visitedTypes.Add(type)) return;

            callerStack.Push($"{type.FullName}");

            if (type.HasFields)
            {
                foreach (var field in type.Fields.Where(f => f.IsPublic))
                {
                    callerStack.Push(field.Name);
                    EnsureDependencies(field.FieldType, callerStack);
                    callerStack.Pop();
                }
            }

            bool IsPublic(PropertyDefinition p)
            {
                var getPublic = p.GetMethod != null && p.GetMethod.IsPublic;
                var setPublic = p.SetMethod != null && p.SetMethod.IsPublic;

                return getPublic || setPublic;
            }

            if (type.HasProperties)
            {
                foreach (var property in type.Properties.Where(IsPublic))
                {
                    callerStack.Push(property.Name);
                    EnsureDependencies(property.PropertyType, callerStack);
                    callerStack.Pop();
                }
            }

            if (type.HasEvents)
            {
                foreach (var evt in type.Events.Where(e => e.AddMethod.IsPublic || e.RemoveMethod.IsPublic))
                {
                    callerStack.Push(evt.Name);
                    EnsureDependencies(evt.EventType, callerStack);
                    callerStack.Pop();
                }
            }

            if (type.HasMethods)
            {
                foreach (var method in type.Methods.Where(m => m.IsPublic))
                {
                    foreach (var parameter in method.Parameters)
                    {
                        callerStack.Push($"{method.Name}({parameter.Name})");
                        EnsureDependencies(parameter.ParameterType, callerStack);
                        callerStack.Pop();
                    }

                    callerStack.Push($"{method.Name}() return type");
                    EnsureDependencies(method.ReturnType, callerStack);
                    callerStack.Pop();
                }
            }

            callerStack.Push("base type");
            EnsureDependencies(type.BaseType, callerStack);
            callerStack.Pop();

            MarkTypePublic(type, callerStack);

            callerStack.Pop();
        }

        private void MarkTypePublic(TypeDefinition type, Stack<string> callerStack)
        {
            bool madeChanges = false;

            if (type.IsNested)
            {
                if (!type.IsNestedPublic)
                {
                    type.IsNestedPublic = true;
                    madeChanges = true;
                }

                callerStack.Push("parent type");
                MarkTypePublic(type.DeclaringType, callerStack);
                callerStack.Pop();
            }
            else
            {
                if (!type.IsPublic)
                {
                    type.IsPublic = true;
                    madeChanges = true;
                }
            }

            if (madeChanges)
            {
                var reason = string.Join($" ->{Environment.NewLine}  ", callerStack.Reverse());
                _logger.Verbose($"Public API: Forcing type {type.Module.Assembly.Name.Name}::{type.FullName} to public because of:{Environment.NewLine}  {reason}");
            }
        }

        private void EnsureDependencies(TypeReference type, Stack<string> callerStack)
        {
            if (type == null) return;

            if (type.IsGenericInstance && type is GenericInstanceType genericType)
            {
                foreach (var argument in genericType.GenericArguments)
                {
                    callerStack.Push($"Generic type argument {argument.Name}");
                    EnsureDependencies(argument, callerStack);
                    callerStack.Pop();
                }
            }

            if (type.Scope != _repackContext.TargetAssemblyMainModule) return;

            var definition = type.Resolve();
            if (definition != null)
            {
                EnsureDependencies(definition, callerStack);
            }
        }
    }
}
