﻿//
// Copyright (c) 2011 Francois Valdy
// Copyright (c) 2015 Timotei Dolean
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using ILRepacking.Mixins;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ILRepacking
{
    internal class RepackImporter : IRepackImporter, IRepackCopier
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly RepackOptions _options;
        private readonly Dictionary<AssemblyDefinition, int> _aspOffsets;
        private readonly Dictionary<ImportDebugInformation, ImportDebugInformation> _importDebugInformations = new();
        private readonly static Instruction _dummyInstruction = Instruction.Create(OpCodes.Nop);

        const string ExcludeInternalizeAttName = "RepackExcludeInternalizeAttribute";

        public RepackImporter(
            ILogger logger,
            RepackOptions options,
            IRepackContext repackContext,
            Dictionary<AssemblyDefinition, int> aspOffsets)
        {
            _logger = logger;
            _options = options;
            _repackContext = repackContext;
            _aspOffsets = aspOffsets;
        }

        public void Import(ExportedType type, Collection<ExportedType> exportedTypesCollection, ModuleDefinition targetAssemblyMainModule)
        {
            var scope = default(IMetadataScope);

            // try to skip redirects to merged assemblies
            if (type.Scope is AssemblyNameReference)
            {
                if (_repackContext.MergedAssemblies.Any(x => x.Name.Name == ((AssemblyNameReference)type.Scope).Name))
                {
                    return;
                }

                scope = _repackContext.PlatformFixer.FixPlatformVersion(((AssemblyNameReference)type.Scope));
            }
            else if (type.Scope is ModuleReference)
            {
                if (_repackContext.MergedAssemblies.SelectMany(x => x.Modules).Any(x => x.Name == ((ModuleReference)type.Scope).Name))
                {
                    return;
                }

                // TODO fix scope (should probably be added to target ModuleReferences, otherwise metadatatoken will be wrong)
                // I've never seen an exported type redirected to a module, doing so would be blind guessing
                scope = type.Scope;
            }

            if (type.IsForwarder)
            {
                // Skip duplicated forwarders
                var fullName = type.FullName;
                if (exportedTypesCollection.Any(t => t.IsForwarder && t.FullName == fullName))
                {
                    return;
                }
            }

            var newExportedType = new ExportedType(type.Namespace, type.Name, targetAssemblyMainModule, scope)
            {
                Attributes = type.Attributes,
                Identifier = type.Identifier, // TODO: CHECK THIS when merging multiple assemblies when exported types ?
                DeclaringType = type.DeclaringType
            };

            exportedTypesCollection.Add(newExportedType);
        }

        public TypeReference Import(TypeReference reference, IGenericParameterProvider context)
        {
            TypeDefinition type = _repackContext.GetMergedTypeFromTypeRef(reference);
            if (type != null)
                return type;

            _repackContext.PlatformFixer.FixPlatformVersion(reference);
            try
            {
                if (context == null)
                {
                    // we come here when importing types used for assembly-level custom attributes
                    return _repackContext.TargetAssemblyMainModule.ImportReference(reference);
                }
                return _repackContext.TargetAssemblyMainModule.ImportReference(reference, context);
            }
            catch (ArgumentOutOfRangeException) // working around a bug in Cecil
            {
                _logger.Error("Problem adding reference: " + reference.FullName);
                throw;
            }
        }

        public FieldReference Import(FieldReference reference, IGenericParameterProvider context)
        {
            _repackContext.PlatformFixer.FixPlatformVersion(reference);

            return _repackContext.TargetAssemblyMainModule.ImportReference(reference, context);
        }

        public MethodReference Import(MethodReference reference)
        {
            _repackContext.PlatformFixer.FixPlatformVersion(reference);
            return _repackContext.TargetAssemblyMainModule.ImportReference(reference);
        }

        public MethodReference Import(MethodReference reference, IGenericParameterProvider context)
        {
            // If this is a Method/TypeDefinition, it will be corrected to a definition again later

            _repackContext.PlatformFixer.FixPlatformVersion(reference);
            return _repackContext.TargetAssemblyMainModule.ImportReference(reference, context);
        }

        private static bool AreTypesEqualByName(TypeDefinition t1, TypeDefinition t2)
        {
            if (t1 == null && t2 == null)
            {
                return true;
            }

            if (t1 == null || t2 == null)
            {
                return false;
            }

            if (t1.Name != t2.Name)
            {
                return false;
            }

            if (t1.Namespace != t2.Namespace)
            {
                return false;
            }

            return AreTypesEqualByName(t1.DeclaringType, t2.DeclaringType);
        }

        public TypeDefinition Import(TypeDefinition type, Collection<TypeDefinition> col, bool internalize)
        {
            _logger.Verbose("- Importing " + type);
            if (ShouldDrop(type))
            {
                return null;
            }

            TypeDefinition nt = _repackContext.TargetAssemblyMainModule.Types.FirstOrDefault(x => AreTypesEqualByName(x, type));
            bool justCreatedType = false;
            if (nt == null)
            {
                nt = CreateType(type, col, internalize, null);
                justCreatedType = true;

                if (IsWellKnownType(type))
                {
                    internalize = false;
                }
            }
            else if (DuplicateTypeAllowed(type))
            {
                _logger.Verbose("Merging " + type);
                internalize = false;
            }
            else if (!type.IsPublic || internalize)
            {
                var originalModule = _repackContext.MappingHandler.GetOriginalModule(nt);
                _logger.Verbose($"- Renaming previously imported type {nt.FullName} from {originalModule.Name}");
                
                // rename the type previously imported.
                // renaming the new one before import made Cecil throw an exception.
                string other = GenerateName(nt, originalModule?.Mvid.ToString());
                
                // Check whether renamed type already exists
                TypeDefinition otherNt = _repackContext.TargetAssemblyMainModule.Types.FirstOrDefault(x =>
                    x.Name == other &&
                    x.Namespace == nt.Namespace &&
                    AreTypesEqualByName(x.DeclaringType, nt.DeclaringType));
                if (otherNt != null)
                {
                    var otherOriginalModule = _repackContext.MappingHandler.GetOriginalModule(otherNt);
                    _logger.Verbose($"- Collision found with type {otherNt.FullName} from {otherOriginalModule.Name}. Renaming now to a random name");
                    // Create a random name
                    other = GenerateName(nt);
                }

                _logger.Verbose($"- Renaming {nt.FullName} from {originalModule.Name} into {nt.Namespace}.{other}");
                nt.Name = other;
                nt = CreateType(type, col, internalize, null);
                justCreatedType = true;
            }
            else if (_options.UnionMerge)
            {
                _logger.Verbose("Merging " + type);
                internalize = false;
            }
            else
            {
                _logger.Error("Duplicate type " + type);
                throw new InvalidOperationException(
                    "Duplicate type " + type + " from " + type.Scope + ", was also present in " +
                    MappingHandler.GetScopeFullName(_repackContext.MappingHandler.GetOrigTypeScope<IMetadataScope>(nt)));
            }
            _repackContext.MappingHandler.StoreRemappedType(type, nt);

            // nested types first (are never internalized)
            foreach (TypeDefinition nested in type.NestedTypes)
            {
                if (ShouldDrop(nested) == false)
                {
                    Import(nested, nt.NestedTypes, false);
                }
            }
            foreach (FieldDefinition field in type.Fields)
            {
                if (ShouldDrop(field) == false)
                {
                    CloneTo(field, nt);
                }
            }
            // methods before fields / events
            foreach (MethodDefinition meth in type.Methods)
            {
                if (ShouldDrop(meth) == false)
                {
                    CloneTo(meth, nt, justCreatedType);
                }
            }
            foreach (EventDefinition evt in type.Events)
            {
                if (ShouldDrop(evt) == false)
                {
                    CloneTo(evt, nt, nt.Events);
                }
            }
            foreach (PropertyDefinition prop in type.Properties)
            {
                if (ShouldDrop(prop) == false)
                {
                    CloneTo(prop, nt, nt.Properties);
                }
            }

            if (internalize && _options.RenameInternalized)
            {
                string newName = GenerateName(nt, type.Module.Mvid.ToString());
                _logger.Verbose("Renaming " + nt.FullName + " into " + newName);
                nt.Name = newName;
            }

            return nt;
        }

        private string GenerateName(TypeDefinition typeDefinition, string disambiguator = null)
        {
            disambiguator ??= Guid.NewGuid().ToString();
            return $"<{disambiguator}>{typeDefinition.Name}";
        }

        private bool ShouldDrop<TMember>(TMember member) where TMember : ICustomAttributeProvider, IMemberDefinition
        {
            var dropAttributes = _options.RepackDropAttributes;
            if (!dropAttributes.Any())
            {
                return false;
            }

            if (!member.HasCustomAttributes)
            {
                return false;
            }

            // skip members marked with a custom attribute named as /repackdrop:RepackDropAttribute
            var dropAttribute = member.CustomAttributes.FirstOrDefault(attr => 
                dropAttributes.Contains(attr.AttributeType.Name) ||
                dropAttributes.Contains(attr.AttributeType.FullName));
            if (dropAttribute != null)
            {
                _logger.Verbose("Repack dropped " + typeof(TMember).Name + ": " + member.FullName + " as it was marked with " + dropAttribute.AttributeType.FullName);
                return true;
            }

            return false;
        }

        // Real stuff below //
        // These methods are somehow a merge between the clone methods of Cecil 0.6 and the import ones of 0.9
        // They use Cecil's MetaDataImporter to rebase imported stuff into the new assembly, but then another pass is required
        //  to clean the TypeRefs Cecil keeps around (although the generated IL would be kind-o valid without, whatever 'valid' means)

        /// <summary>
        /// Clones a field to a newly created type
        /// </summary>
        private void CloneTo(FieldDefinition field, TypeDefinition nt)
        {
            if (nt.Fields.Any(x => x.Name == field.Name))
            {
                return;
            }
            FieldDefinition nf = new FieldDefinition(field.Name, field.Attributes, Import(field.FieldType, nt));
            nt.Fields.Add(nf);

            if (field.HasConstant)
                nf.Constant = field.Constant;

            if (field.HasMarshalInfo)
                nf.MarshalInfo = field.MarshalInfo;

            if (field.InitialValue != null && field.InitialValue.Length > 0)
                nf.InitialValue = field.InitialValue;

            if (field.HasLayoutInfo)
                nf.Offset = field.Offset;

            CopyCustomAttributes(field.CustomAttributes, nf.CustomAttributes, nt);
        }

        /// <summary>
        /// Clones a parameter into a newly created method
        /// </summary>
        private void CloneTo(ParameterDefinition param, MethodDefinition context, Collection<ParameterDefinition> col)
        {
            ParameterDefinition pd = new ParameterDefinition(param.Name, param.Attributes, Import(param.ParameterType, context));
            if (param.HasConstant)
                pd.Constant = param.Constant;
            if (param.HasMarshalInfo)
                pd.MarshalInfo = param.MarshalInfo;
            if (param.HasCustomAttributes)
                CopyCustomAttributes(param.CustomAttributes, pd.CustomAttributes, context);
            col.Add(pd);
        }

        private void CloneTo(EventDefinition evt, TypeDefinition nt, Collection<EventDefinition> col)
        {
            // ignore duplicate event
            if (nt.Events.Any(x => x.Name == evt.Name))
            {
                return;
            }

            EventDefinition ed = new EventDefinition(evt.Name, evt.Attributes, Import(evt.EventType, nt));
            col.Add(ed);
            if (evt.AddMethod != null)
                ed.AddMethod = FindMethodInNewType(nt, evt.AddMethod);
            if (evt.RemoveMethod != null)
                ed.RemoveMethod = FindMethodInNewType(nt, evt.RemoveMethod);
            if (evt.InvokeMethod != null)
                ed.InvokeMethod = FindMethodInNewType(nt, evt.InvokeMethod);
            if (evt.HasOtherMethods)
            {
                foreach (MethodDefinition meth in evt.OtherMethods)
                {
                    var nm = FindMethodInNewType(nt, meth);
                    if (nm != null)
                        ed.OtherMethods.Add(nm);
                }
            }

            CopyCustomAttributes(evt.CustomAttributes, ed.CustomAttributes, nt);
        }

        private void CloneTo(PropertyDefinition prop, TypeDefinition nt, Collection<PropertyDefinition> col)
        {
            // ignore duplicate property
            var others = nt.Properties.Where(x => x.Name == prop.Name).ToList();
            if (others.Any())
            {
                bool skip = false;
                if (!IsIndexer(prop) || !IsIndexer(others.First()))
                {
                    skip = true;
                }
                else
                {
                    // "Item" property is used to implement Indexer operators
                    // It may be specified more than one, with extra arguments to get/set methods
                    // Note than one may also define a standard "Item" property, in which case he won't be able to define Indexers

                    // Here we try to prevent duplicate indexers, but allow to merge non-duplicated ones (e.g. this[int] & this[string] )
                    var args = ExtractIndexerParameters(prop);
                    if (others.Any(x => _repackContext.ReflectionHelper.AreSame(args, ExtractIndexerParameters(x))))
                    {
                        skip = true;
                    }
                }
                if (skip)
                {
                    return;
                }
            }

            PropertyDefinition pd = new PropertyDefinition(prop.Name, prop.Attributes, Import(prop.PropertyType, nt));
            col.Add(pd);
            if (prop.SetMethod != null)
                pd.SetMethod = FindMethodInNewType(nt, prop.SetMethod);
            if (prop.GetMethod != null)
                pd.GetMethod = FindMethodInNewType(nt, prop.GetMethod);
            if (prop.HasOtherMethods)
            {
                foreach (MethodDefinition meth in prop.OtherMethods)
                {
                    var nm = FindMethodInNewType(nt, meth);
                    if (nm != null)
                        pd.OtherMethods.Add(nm);
                }
            }

            CopyCustomAttributes(prop.CustomAttributes, pd.CustomAttributes, nt);
        }

        private void CloneTo(MethodDefinition meth, TypeDefinition type, bool typeJustCreated)
        {
            // ignore duplicate method for merged duplicated types
            if (!typeJustCreated &&
                type.Methods.Count > 0 &&
                type.Methods.Any(x =>
                  (x.Name == meth.Name) &&
                  (x.Parameters.Count == meth.Parameters.Count) &&
                  (x.ToString() == meth.ToString()))) // TODO: better/faster comparation of parameter types?
            {
                return;
            }
            // use void placeholder as we'll do the return type import later on (after generic parameters)
            MethodDefinition nm = new MethodDefinition(meth.Name, meth.Attributes, _repackContext.TargetAssemblyMainModule.TypeSystem.Void);
            nm.ImplAttributes = meth.ImplAttributes;
            if (meth.DebugInformation.HasCustomDebugInformations)
                nm.DebugInformation.CustomDebugInformations.AddRange(meth.DebugInformation.CustomDebugInformations);
            if (meth.DebugInformation.HasSequencePoints)
                nm.DebugInformation.SequencePoints.AddRange(meth.DebugInformation.SequencePoints);

            type.Methods.Add(nm);

            CopyGenericParameters(meth.GenericParameters, nm.GenericParameters, nm);

            if (meth.HasPInvokeInfo)
            {
                if (meth.PInvokeInfo == null)
                {
                    // Even if this was allowed, I'm not sure it'd work out
                    //nm.RVA = meth.RVA;
                }
                else
                {
                    nm.PInvokeInfo = new PInvokeInfo(meth.PInvokeInfo.Attributes, meth.PInvokeInfo.EntryPoint, meth.PInvokeInfo.Module);
                }
            }

            foreach (ParameterDefinition param in meth.Parameters)
                CloneTo(param, nm, nm.Parameters);

            foreach (MethodReference ov in meth.Overrides)
                nm.Overrides.Add(Import(ov, nm));

            CopySecurityDeclarations(meth.SecurityDeclarations, nm.SecurityDeclarations, nm);
            CopyCustomAttributes(meth.CustomAttributes, nm.CustomAttributes, nm);

            nm.ReturnType = Import(meth.ReturnType, nm);
            nm.MethodReturnType.Attributes = meth.MethodReturnType.Attributes;
            if (meth.MethodReturnType.HasConstant)
                nm.MethodReturnType.Constant = meth.MethodReturnType.Constant;
            if (meth.MethodReturnType.HasMarshalInfo)
                nm.MethodReturnType.MarshalInfo = meth.MethodReturnType.MarshalInfo;
            if (meth.MethodReturnType.HasCustomAttributes)
                CopyCustomAttributes(meth.MethodReturnType.CustomAttributes, nm.MethodReturnType.CustomAttributes, nm);

            if (meth.HasBody)
                CloneTo(meth.Body, nm);

            nm.DebugInformation.Scope = CopyScope(meth.DebugInformation.Scope, nm, out _);

            meth.Body = null; // frees memory

            nm.IsAddOn = meth.IsAddOn;
            nm.IsRemoveOn = meth.IsRemoveOn;
            nm.IsGetter = meth.IsGetter;
            nm.IsSetter = meth.IsSetter;
            nm.CallingConvention = meth.CallingConvention;
        }

        private ScopeDebugInformation CopyScope(ScopeDebugInformation scope, MethodDefinition nm, out bool copied)
        {
            copied = false;
            if (scope is null || scope.Import is null && !scope.HasConstants && !scope.HasScopes)
                return scope;

            var ns = new ScopeDebugInformation(_dummyInstruction, null);
            ns.Start = new InstructionOffset(scope.Start.Offset);
            ns.End = scope.End.IsEndOfMethod ? default : new InstructionOffset(scope.End.Offset);
            if (scope.HasCustomDebugInformations)
                ns.CustomDebugInformations.AddRange(scope.CustomDebugInformations);
            if (scope.HasVariables)
                ns.Variables.AddRange(scope.Variables);
            if (scope.HasScopes)
                foreach (var ps in scope.Scopes)
                {
                    ns.Scopes.Add(CopyScope(ps, nm, out var nc));
                    copied |= nc;
                }
            if (scope.HasConstants)
            {
                copied = true;
                foreach (var pc in scope.Constants)
                {
                    var nc = new ConstantDebugInformation(pc.Name, Import(pc.ConstantType, nm), pc.Value);
                    if (pc.HasCustomDebugInformations)
                        nc.CustomDebugInformations.AddRange(pc.CustomDebugInformations);
                    ns.Constants.Add(nc);
                }
            }
            if (scope.Import is not null)
            {
                copied = true;
                ns.Import = CopyImport(scope.Import, nm);
            }

            return copied ? ns : scope;
        }

        private ImportDebugInformation CopyImport(ImportDebugInformation import, MethodDefinition nm)
        {
            if (import is null)
                return null;
            if (_importDebugInformations.TryGetValue(import, out var ni))
                return ni;

            ni = new ImportDebugInformation();
            ni.Parent = CopyImport(import.Parent, nm);
            if (import.HasCustomDebugInformations)
                ni.CustomDebugInformations.AddRange(import.CustomDebugInformations);
            if (import.HasTargets)
                foreach (var pt in import.Targets)
                {
                    var nt = new ImportTarget(pt.Kind);
                    nt.Alias = pt.Alias;
                    nt.Namespace = pt.Namespace;
                    if (pt.Type is not null)
                        nt.Type = Import(pt.Type, nm);
                    if (pt.AssemblyReference is not null)
                        nt.AssemblyReference = _repackContext.PlatformFixer.FixPlatformVersion(pt.AssemblyReference) as AssemblyNameReference;
                    ni.Targets.Add(nt);
                }
            _importDebugInformations.Add(import, ni);
            return ni;
        }

        private void CloneTo(MethodBody body, MethodDefinition parent)
        {
            MethodBody nb = new MethodBody(parent);
            parent.Body = nb;

            nb.MaxStackSize = body.MaxStackSize;
            nb.InitLocals = body.InitLocals;
            nb.LocalVarToken = body.LocalVarToken;

            foreach (VariableDefinition var in body.Variables)
                nb.Variables.Add(new VariableDefinition(
                    Import(var.VariableType, parent)));

            nb.Instructions.Capacity = Math.Max(nb.Instructions.Capacity, body.Instructions.Count);
            _repackContext.LineIndexer.PreMethodBodyRepack(body, parent);
            foreach (Instruction instr in body.Instructions)
            {
                Instruction ni;

                if (instr.OpCode.Code == Code.Calli)
                {
                    var callSite = (CallSite)instr.Operand;
                    CallSite ncs = new CallSite(Import(callSite.ReturnType, parent))
                    {
                        HasThis = callSite.HasThis,
                        ExplicitThis = callSite.ExplicitThis,
                        CallingConvention = callSite.CallingConvention
                    };
                    foreach (ParameterDefinition param in callSite.Parameters)
                        CloneTo(param, parent, ncs.Parameters);
                    ni = Instruction.Create(instr.OpCode, ncs);
                }
                else switch (instr.OpCode.OperandType)
                    {
                        case OperandType.InlineArg:
                        case OperandType.ShortInlineArg:
                            if (instr.Operand == body.ThisParameter)
                            {
                                ni = Instruction.Create(instr.OpCode, nb.ThisParameter);
                            }
                            else
                            {
                                int param = body.Method.Parameters.IndexOf((ParameterDefinition)instr.Operand);
                                ni = Instruction.Create(instr.OpCode, parent.Parameters[param]);
                            }
                            break;
                        case OperandType.InlineVar:
                        case OperandType.ShortInlineVar:
                            int var = body.Variables.IndexOf((VariableDefinition)instr.Operand);
                            ni = Instruction.Create(instr.OpCode, nb.Variables[var]);
                            break;
                        case OperandType.InlineField:
                            ni = Instruction.Create(instr.OpCode, Import((FieldReference)instr.Operand, parent));
                            break;
                        case OperandType.InlineMethod:
                            ni = Instruction.Create(instr.OpCode, Import((MethodReference)instr.Operand, parent));
                            FixAspNetOffset(nb.Instructions, (MethodReference)instr.Operand, parent);
                            break;
                        case OperandType.InlineType:
                            ni = Instruction.Create(instr.OpCode, Import((TypeReference)instr.Operand, parent));
                            break;
                        case OperandType.InlineTok:
                            if (instr.Operand is TypeReference)
                                ni = Instruction.Create(instr.OpCode, Import((TypeReference)instr.Operand, parent));
                            else if (instr.Operand is FieldReference)
                                ni = Instruction.Create(instr.OpCode, Import((FieldReference)instr.Operand, parent));
                            else if (instr.Operand is MethodReference)
                                ni = Instruction.Create(instr.OpCode, Import((MethodReference)instr.Operand, parent));
                            else
                                throw new InvalidOperationException();
                            break;
                        case OperandType.ShortInlineBrTarget:
                        case OperandType.InlineBrTarget:
                            ni = Instruction.Create(instr.OpCode, (Instruction)instr.Operand);
                            break;
                        case OperandType.InlineSwitch:
                            ni = Instruction.Create(instr.OpCode, (Instruction[])instr.Operand);
                            break;
                        case OperandType.InlineR:
                            ni = Instruction.Create(instr.OpCode, (double)instr.Operand);
                            break;
                        case OperandType.ShortInlineR:
                            ni = Instruction.Create(instr.OpCode, (float)instr.Operand);
                            break;
                        case OperandType.InlineNone:
                            ni = Instruction.Create(instr.OpCode);
                            break;
                        case OperandType.InlineString:
                            ni = Instruction.Create(instr.OpCode, (string)instr.Operand);
                            break;
                        case OperandType.ShortInlineI:
                            if (instr.OpCode == OpCodes.Ldc_I4_S)
                                ni = Instruction.Create(instr.OpCode, (sbyte)instr.Operand);
                            else
                                ni = Instruction.Create(instr.OpCode, (byte)instr.Operand);
                            break;
                        case OperandType.InlineI8:
                            ni = Instruction.Create(instr.OpCode, (long)instr.Operand);
                            break;
                        case OperandType.InlineI:
                            ni = Instruction.Create(instr.OpCode, (int)instr.Operand);
                            break;
                        default:
                            throw new InvalidOperationException();
                    }
                nb.Instructions.Add(ni);
            }

            for (int i = 0; i < body.Instructions.Count; i++)
            {
                Instruction instr = nb.Instructions[i];
                switch (instr.OpCode.OperandType)
                {
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.InlineBrTarget:
                        instr.Operand = GetInstruction(body, nb, (Instruction)body.Instructions[i].Operand);
                        break;
                    case OperandType.InlineSwitch:
                        instr.Operand = ((Instruction[])body.Instructions[i].Operand).Select(op => GetInstruction(body, nb, op)).ToArray();
                        break;
                    default:
                        break;
                }
            }

            foreach (ExceptionHandler eh in body.ExceptionHandlers)
            {
                ExceptionHandler neh = new ExceptionHandler(eh.HandlerType);
                neh.TryStart = GetInstruction(body, nb, eh.TryStart);
                neh.TryEnd = GetInstruction(body, nb, eh.TryEnd);
                neh.HandlerStart = GetInstruction(body, nb, eh.HandlerStart);
                neh.HandlerEnd = GetInstruction(body, nb, eh.HandlerEnd);

                switch (eh.HandlerType)
                {
                    case ExceptionHandlerType.Catch:
                        neh.CatchType = Import(eh.CatchType, parent);
                        break;
                    case ExceptionHandlerType.Filter:
                        neh.FilterStart = GetInstruction(body, nb, eh.FilterStart);
                        break;
                }

                nb.ExceptionHandlers.Add(neh);
            }
        }

        private TypeDefinition CreateType(TypeDefinition type, Collection<TypeDefinition> col, bool internalize, string rename)
        {
            TypeDefinition nt = new TypeDefinition(type.Namespace, rename ?? type.Name, type.Attributes);
            col.Add(nt);

            // only top-level types are internalized
            if (internalize && (nt.DeclaringType == null) && nt.IsPublic && !type.CustomAttributes.Any(x => x.AttributeType.Name == ExcludeInternalizeAttName))
                nt.IsPublic = false;

            CopyGenericParameters(type.GenericParameters, nt.GenericParameters, nt);
            if (type.BaseType != null)
                nt.BaseType = Import(type.BaseType, nt);

            if (type.HasLayoutInfo)
            {
                nt.ClassSize = type.ClassSize;
                nt.PackingSize = type.PackingSize;
            }
            // don't copy these twice if UnionMerge==true
            // TODO: we can move this down if we chek for duplicates when adding
            CopySecurityDeclarations(type.SecurityDeclarations, nt.SecurityDeclarations, nt);
            CopyInterfaces(type.Interfaces, nt.Interfaces, nt);
            CopyCustomAttributes(type.CustomAttributes, nt.CustomAttributes, nt);
            return nt;
        }

        private void CopyInterfaces(Collection<InterfaceImplementation> interfaces1, Collection<InterfaceImplementation> interfaces2, TypeDefinition nt)
        {
            foreach (var iface in interfaces1)
            {
                var newIface = new InterfaceImplementation(Import(iface.InterfaceType, nt));
                CopyCustomAttributes(iface.CustomAttributes, newIface.CustomAttributes, nt);
                interfaces2.Add(newIface);
            }
        }

        private MethodDefinition FindMethodInNewType(TypeDefinition nt, MethodDefinition methodDefinition)
        {
            var ret = _repackContext.ReflectionHelper.FindMethodDefinitionInType(nt, methodDefinition);
            if (ret == null)
            {
                _logger.Warn("Method '" + methodDefinition.FullName + "' not found in merged type '" + nt.FullName + "'");
            }
            return ret;
        }

        private void FixAspNetOffset(Collection<Instruction> instructions, MethodReference operand, MethodDefinition parent)
        {
            if (operand.Name == "WriteUTF8ResourceString" || operand.Name == "CreateResourceBasedLiteralControl")
            {
                var fullName = operand.FullName;
                if (fullName == "System.Void System.Web.UI.TemplateControl::WriteUTF8ResourceString(System.Web.UI.HtmlTextWriter,System.Int32,System.Int32,System.Boolean)" ||
                    fullName == "System.Web.UI.LiteralControl System.Web.UI.TemplateControl::CreateResourceBasedLiteralControl(System.Int32,System.Int32,System.Boolean)")
                {
                    int offset;
                    if (_aspOffsets.TryGetValue(parent.Module.Assembly, out offset))
                    {
                        int prev = (int)instructions[instructions.Count - 4].Operand;
                        instructions[instructions.Count - 4].Operand = prev + offset;
                    }
                }
            }
        }

        private static bool IsIndexer(PropertyDefinition prop)
        {
            if (prop.Name != "Item" && !prop.Name.EndsWith(".Item")) // cover explicitely implemented properties
                return false;
            var parameters = ExtractIndexerParameters(prop);
            return parameters != null && parameters.Count > 0;
        }

        private static IList<ParameterDefinition> ExtractIndexerParameters(PropertyDefinition prop)
        {
            if (prop.GetMethod != null)
                return prop.GetMethod.Parameters;
            if (prop.SetMethod != null)
                return prop.SetMethod.Parameters.ToList().GetRange(0, prop.SetMethod.Parameters.Count - 1);
            return null;
        }

        private static Instruction GetInstruction(MethodBody oldBody, MethodBody newBody, Instruction i)
        {
            int pos = oldBody.Instructions.IndexOf(i);
            if (pos > -1 && pos < newBody.Instructions.Count)
                return newBody.Instructions[pos];

            return null /*newBody.Instructions.Outside*/;
        }

        // https://github.com/dotnet/roslyn/blob/ee2526876b7bff3380bc110d819dda23cac668a5/src/Compilers/CSharp/Portable/Symbols/EmbeddableAttributes.cs#L10
        private static readonly HashSet<string> allowUnifyTypeNames = new HashSet<string>
        {
            "System.Runtime.CompilerServices.IsReadOnlyAttribute",
            "System.Runtime.CompilerServices.IsByRefLikeAttribute",
            "System.Runtime.CompilerServices.IsUnmanagedAttribute",
            "System.Runtime.CompilerServices.NullableAttribute",
            "System.Runtime.CompilerServices.NullableContextAttribute",
            "System.Runtime.CompilerServices.NullablePublicOnlyAttribute",
            "System.Runtime.CompilerServices.NativeIntegerAttribute",
            "System.Runtime.CompilerServices.ScopedRefAttribute",
            "System.Runtime.CompilerServices.RefSafetyRulesAttribute",
            "System.Runtime.CompilerServices.RequiresLocationAttribute",
            "Microsoft.CodeAnalysis.EmbeddedAttribute"
        };

        private bool DuplicateTypeAllowed(TypeDefinition type)
        {
            if (IsWellKnownType(type))
            {
                return true;
            }

            if (_options.AllowAllDuplicateTypes || _options.AllowedDuplicateTypes.Contains(type.FullName))
                return true;

            var top = type;
            while (top.IsNested)
                top = top.DeclaringType;
            string nameSpace = top.Namespace;
            if (!String.IsNullOrEmpty(nameSpace) && _options.AllowedDuplicateNameSpaces.Any(s => s == nameSpace || nameSpace.StartsWith(s + ".")))
                return true;

            return false;
        }

        private bool IsWellKnownType(TypeDefinition type)
        {
            string fullName = type.FullName;

            // Merging module because IKVM uses this class to store some fields.
            // Doesn't fully work yet, as IKVM is nice enough to give all the fields the same name...
            if (fullName == "<Module>" || fullName == "__<Proxy>")
                return true;

            // XAML helper class, identical in all assemblies, unused within the assembly, and instantiated through reflection from the outside
            // We could just skip them after the first one, but merging them works just fine
            if (fullName == "XamlGeneratedNamespace.GeneratedInternalTypeHelper")
                return true;

            // Merge should be OK since member's names are pretty unique,
            // but renaming duplicate members would be safer...
            if (fullName == "<PrivateImplementationDetails>" && type.IsPublic)
                return true;

            if (allowUnifyTypeNames.Contains(fullName))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clones a collection of SecurityDeclarations
        /// </summary>
        public void CopySecurityDeclarations(Collection<SecurityDeclaration> input, Collection<SecurityDeclaration> output, IGenericParameterProvider context)
        {
            foreach (SecurityDeclaration sec in input)
            {
                SecurityDeclaration newSec = null;
                if (PermissionsetHelper.IsXmlPermissionSet(sec))
                {
                    newSec = PermissionsetHelper.Xml2PermissionSet(sec, _repackContext.TargetAssemblyMainModule);
                }
                if (newSec == null)
                {
                    newSec = new SecurityDeclaration(sec.Action);
                    foreach (SecurityAttribute sa in sec.SecurityAttributes)
                    {
                        SecurityAttribute newSa = new SecurityAttribute(Import(sa.AttributeType, context));
                        if (sa.HasFields)
                        {
                            foreach (CustomAttributeNamedArgument cana in sa.Fields)
                            {
                                newSa.Fields.Add(Copy(cana, context));
                            }
                        }
                        if (sa.HasProperties)
                        {
                            foreach (CustomAttributeNamedArgument cana in sa.Properties)
                            {
                                newSa.Properties.Add(Copy(cana, context));
                            }
                        }
                        newSec.SecurityAttributes.Add(newSa);
                    }
                }
                output.Add(newSec);
            }
        }

        // helper
        private static void Copy<T>(Collection<T> input, Collection<T> output, Action<T, T> action)
        {
            if (input.Count != output.Count)
                throw new InvalidOperationException();
            for (int i = 0; i < input.Count; i++)
            {
                action.Invoke(input[i], output[i]);
            }
        }

        public void CopyGenericParameters(Collection<GenericParameter> input, Collection<GenericParameter> output, IGenericParameterProvider nt)
        {
            foreach (GenericParameter gp in input)
            {
                GenericParameter ngp = new GenericParameter(gp.Name, nt);

                ngp.Attributes = gp.Attributes;
                output.Add(ngp);
            }
            // delay copy to ensure all generics parameters are already present
            Copy(input, output, (gp, ngp) => CopyTypeReferences(gp.Constraints, ngp.Constraints, nt));
            Copy(input, output, (gp, ngp) => CopyCustomAttributes(gp.CustomAttributes, ngp.CustomAttributes, nt));
        }

        public void CopyCustomAttributes(Collection<CustomAttribute> input, Collection<CustomAttribute> output, IGenericParameterProvider context)
        {
            CopyCustomAttributes(input, output, true, context);
        }

        public CustomAttribute Copy(CustomAttribute ca, IGenericParameterProvider context)
        {
            CustomAttribute newCa = new CustomAttribute(Import(ca.Constructor));
            foreach (var arg in ca.ConstructorArguments)
                newCa.ConstructorArguments.Add(Copy(arg, context));
            foreach (var arg in ca.Fields)
                newCa.Fields.Add(Copy(arg, context));
            foreach (var arg in ca.Properties)
                newCa.Properties.Add(Copy(arg, context));
            return newCa;
        }

        public void CopyCustomAttributes(Collection<CustomAttribute> input, Collection<CustomAttribute> output, bool allowMultiple, IGenericParameterProvider context)
        {
            var reflectionHelper = _repackContext.ReflectionHelper;
            foreach (CustomAttribute ca in input)
            {
                if (ca.AttributeType.Name == ExcludeInternalizeAttName) continue;

                var caType = ca.AttributeType;
                var similarAttributes = output.Where(attr => reflectionHelper.AreSame(attr.AttributeType, caType)).ToList();
                if (similarAttributes.Count != 0)
                {
                    if (!allowMultiple)
                        continue;
                    if (!CustomAttributeTypeAllowsMultiple(caType))
                        continue;
                    if (similarAttributes.Any(x =>
                            reflectionHelper.AreSame(x.ConstructorArguments, ca.ConstructorArguments) &&
                            reflectionHelper.AreSame(x.Fields, ca.Fields) &&
                            reflectionHelper.AreSame(x.Properties, ca.Properties)
                        ))
                        continue;
                }
                output.Add(Copy(ca, context));
            }
        }

        private bool CustomAttributeTypeAllowsMultiple(TypeReference type)
        {
            if (type.FullName == "IKVM.Attributes.JavaModuleAttribute" || type.FullName == "IKVM.Attributes.PackageListAttribute")
            {
                // IKVM module attributes, although they don't allow multiple, IKVM supports the attribute being specified multiple times
                return true;
            }
            TypeDefinition typeDef = type.Resolve();
            if (typeDef != null)
            {
                var ca = typeDef.CustomAttributes.FirstOrDefault(x => x.AttributeType.FullName == "System.AttributeUsageAttribute");
                if (ca != null)
                {
                    var prop = ca.Properties.FirstOrDefault(y => y.Name == "AllowMultiple");
                    if (prop.Argument.Value is bool)
                    {
                        return (bool)prop.Argument.Value;
                    }
                }
            }
            // default is false
            return false;
        }

        public void CopyTypeReferences(Collection<TypeReference> input, Collection<TypeReference> output, IGenericParameterProvider context)
        {
            foreach (TypeReference ta in input)
            {
                output.Add(Import(ta, context));
            }
        }

        public void CopyTypeReferences(Collection<GenericParameterConstraint> input, Collection<GenericParameterConstraint> output, IGenericParameterProvider context)
        {
            foreach (var gpc in input)
            {
                var result = new GenericParameterConstraint(Import(gpc.ConstraintType, context));
                CopyCustomAttributes(gpc.CustomAttributes, result.CustomAttributes, context);
                output.Add(result);
            }
        }

        public CustomAttributeArgument Copy(CustomAttributeArgument arg, IGenericParameterProvider context)
        {
            return new CustomAttributeArgument(Import(arg.Type, context), ImportCustomAttributeValue(arg.Value, context));
        }

        public CustomAttributeNamedArgument Copy(CustomAttributeNamedArgument namedArg, IGenericParameterProvider context)
        {
            return new CustomAttributeNamedArgument(namedArg.Name, Copy(namedArg.Argument, context));
        }

        private object ImportCustomAttributeValue(object obj, IGenericParameterProvider context)
        {
            if (obj is TypeReference)
                return Import((TypeReference)obj, context);
            if (obj is CustomAttributeArgument)
                return Copy((CustomAttributeArgument)obj, context);
            if (obj is CustomAttributeArgument[])
                return Array.ConvertAll((CustomAttributeArgument[])obj, a => Copy(a, context));
            return obj;
        }
    }
}
