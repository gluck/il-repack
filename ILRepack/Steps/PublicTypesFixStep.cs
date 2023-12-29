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
            _logger.Info("Processing public types tree");

            var publicTypes = _repackContext.TargetAssemblyMainModule.Types.Where(t => t.IsPublic);

            foreach (var type in publicTypes)
            {
                EnsureDependencies(type);
            }
        }

        private void EnsureDependencies(TypeDefinition type)
        {
            if (type == null) return;

            if (!_visitedTypes.Add(type)) return;

            if (type.HasFields)
            {
                foreach (var field in type.Fields.Where(f => f.IsPublic))
                {
                    EnsureDependencies(field.FieldType);
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
                    EnsureDependencies(property.PropertyType);
                }
            }

            if (type.HasEvents)
            {
                foreach (var evt in type.Events.Where(e => e.AddMethod.IsPublic || e.RemoveMethod.IsPublic))
                {
                    EnsureDependencies(evt.EventType);
                }
            }

            if (type.HasMethods)
            {
                foreach (var method in type.Methods.Where(m => m.IsPublic))
                {
                    foreach (var parameter in method.Parameters)
                    {
                        EnsureDependencies(parameter.ParameterType);
                    }
                    EnsureDependencies(method.ReturnType);
                }
            }

            EnsureDependencies(type.BaseType);

            MarkTypePublic(type);
        }

        private void MarkTypePublic(TypeDefinition type)
        {
            if (type.IsNested)
            {
                type.IsNestedPublic = true;
                MarkTypePublic(type.DeclaringType);
            }
            else
            {
                type.IsPublic = true;
            }
        }

        private void EnsureDependencies(TypeReference type)
        {
            if (type == null) return;

            if (type.IsGenericInstance && type is GenericInstanceType genericType)
            {
                foreach (var argument in genericType.GenericArguments)
                {
                    EnsureDependencies(argument);
                }
            }

            if (type.Scope != _repackContext.TargetAssemblyMainModule) return;

            var definition = type.Resolve();

            EnsureDependencies(definition);
        }
    }
}
