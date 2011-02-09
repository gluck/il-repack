using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace ILRepack
{
    public class ILRepack
    {
        public string OutputFile { get; set; }
        public bool UnionMerge { get; set; }
        public bool LogEnabled { get; set; }
        public Version Version { get; set; }
        public string KeyFile { get; set; }
        public bool MergeDebugInfo { get; set; }

        [Obsolete("Not implemented yet")]
        public string LogEnabledFile { get; set; }

        public Kind? TargetKind { get; set; }

        private List<AssemblyDefinition> MergedAssemblies;
        private AssemblyDefinition TargetAssemblyDefinition { get; set; }

        // helpers
        private ModuleDefinition MainModule { get { return TargetAssemblyDefinition.MainModule; } }
        private AssemblyDefinition OrigMainAssemblyDefinition { get { return MergedAssemblies[0]; } }
        private ModuleDefinition OrigMainModule { get { return OrigMainAssemblyDefinition.MainModule; } }

        public ILRepack()
        {
            // default values
            LogEnabled = true;
            MergeDebugInfo = true;
        }

        private static bool OptB(string val, bool def)
        {
            return val == null ? def : Boolean.Parse(OptS(val));
        }

        private static string OptS(string val)
        {
            if (val == null) return val;
            return val.Substring(val.IndexOf(':') + 1);
        }
        
        private void Log(object str)
        {
            if (LogEnabled)
            {
                Console.WriteLine(str.ToString());
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            ILRepack repack = new ILRepack();
            try
            {
                // TODO: verify arguments, more arguments
                repack.KeyFile = OptS(args.FirstOrDefault(x => x.StartsWith("/keyfile:")));
                repack.LogEnabled = OptB(args.FirstOrDefault(x => x.StartsWith("/LogEnabled:")), repack.LogEnabled);
                repack.OutputFile = OptS(args.FirstOrDefault(x => x.StartsWith("/out:")));
                repack.UnionMerge = args.Any(x => x == "/union");
                if (args.Any(x => x.StartsWith("/ndebug")))
                    repack.MergeDebugInfo = false;
                if (args.Any(x => x.StartsWith("/ver:")))
                {
                    repack.Version = new Version(OptS(args.First(x => x.StartsWith("/ver:"))));
                }
                // everything that doesn't start with a '/' must be a file to merge (TODO: verify this)
                repack.SetInputAssemblies(args.Where(x => !x.StartsWith("/") && File.Exists(x)).ToArray());
                repack.Repack();
            }
            catch (Exception e)
            {
                repack.Log(e);
                return 1;
            }
            return 0;
        }


        public void SetInputAssemblies(string[] assems)
        {
            MergedAssemblies = new List<AssemblyDefinition>();
            // TODO: this could be parallelized to gain speed
            bool mergedDebugInfo = false;
            foreach (string assembly in assems)
            {
                ReaderParameters rp = new ReaderParameters(ReadingMode.Immediate);
                // read PDB/MDB?
                if (MergeDebugInfo && (File.Exists(Path.ChangeExtension(assembly, "pdb")) || File.Exists(Path.ChangeExtension(assembly, "mdb"))))
                {
                    rp.ReadSymbols = true;
                    mergedDebugInfo = true;
                }
                AssemblyDefinition mergeAsm = AssemblyDefinition.ReadAssembly(assembly, rp);
                MergedAssemblies.Add(mergeAsm);
            }
            // prevent writing PDB if we haven't read any
            MergeDebugInfo = mergedDebugInfo;
        }

        public enum Kind
        {
            Dll,
            Exe,
            WinExe,
            SameAsPrimaryAssembly
        }

        public void Repack()
        {
            ModuleKind kind = OrigMainModule.Kind;
            if (TargetKind.HasValue)
            {
                switch (TargetKind.Value)
                {
                    case Kind.Dll: kind = ModuleKind.Dll; break;
                    case Kind.Exe: kind = ModuleKind.Console; break;
                    case Kind.WinExe: kind = ModuleKind.Windows; break;
                }
            }
            TargetAssemblyDefinition = AssemblyDefinition.CreateAssembly(OrigMainAssemblyDefinition.Name, OrigMainModule.Name,
                new ModuleParameters()
                    {
                        Kind = kind,
                        Architecture = OrigMainModule.Architecture,
                        Runtime = OrigMainModule.Runtime
                    });
            if (Version != null)
                TargetAssemblyDefinition.Name.Version = Version;
            // TODO: Win32 version/icon properties seem not to be copied... limitation in cecil 0.9x?

            INFO("Processing references");
            // Add all references to merged assembly (probably not necessary)
            foreach (var z in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.AssemblyReferences))
            {
                string name = z.Name;
                if (!MergedAssemblies.Any(y => y.Name.Name == name) &&
                    TargetAssemblyDefinition.Name.Name != name &&
                    !TargetAssemblyDefinition.MainModule.AssemblyReferences.Any(y => y.Name == name))
                {
                    // TODO: fix .NET runtime references?
                    // - to target a specific runtime version or
                    // - to target a single version if merged assemblies target different versions
                    VERBOSE("- add reference " + z);
                    TargetAssemblyDefinition.MainModule.AssemblyReferences.Add(z);
                }
            }

            // add all module references (pinvoke dlls)
            foreach (var z in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.ModuleReferences))
            {
                string name = z.Name;
                if (!TargetAssemblyDefinition.MainModule.ModuleReferences.Any(y => y.Name == name))
                {
                    TargetAssemblyDefinition.MainModule.ModuleReferences.Add(z);
                }
            }

            INFO("Processing resources");
            // merge resources
            foreach (var r in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.Resources))
            {
                // TODO: handle duplicate names?
                VERBOSE("- Importing " + r.Name);
                TargetAssemblyDefinition.MainModule.Resources.Add(r);
            }

            INFO("Processing types");
            // merge types
            foreach (var r in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.Types))
            {
                if (r.FullName == "<Module>")
                    continue;
                // TODO: special handling for "<PrivateImplementationDetails>" (always merge this types by adding subtypes) 
                VERBOSE("- Importing " + r);
                Import(r, MainModule.Types);
            }

            CopyCustomAttributes(OrigMainAssemblyDefinition.CustomAttributes, TargetAssemblyDefinition.CustomAttributes);
            CopySecurityDeclarations(OrigMainAssemblyDefinition.SecurityDeclarations, TargetAssemblyDefinition.SecurityDeclarations, null);

            ReferenceFixator fixator = new ReferenceFixator(MainModule);
            if (OrigMainModule.EntryPoint != null)
            {
                MainModule.EntryPoint = fixator.Fix(Import(OrigMainAssemblyDefinition.EntryPoint), null).Resolve();
            }

            INFO("Fixing references");
            // this step travels through all TypeRefs & replaces them by matching TypeDefs

            foreach (var r in MainModule.Types)
            {
                fixator.FixReferences(r);
            }
            fixator.FixReferences(TargetAssemblyDefinition.CustomAttributes, null);

            // final reference cleanup (Cecil Import automatically added them)
            foreach (AssemblyDefinition asm in MergedAssemblies)
            {
                string mergedAssemblyName = asm.Name.Name;
                MainModule.AssemblyReferences.Any(
                    y => y.Name == mergedAssemblyName && MainModule.AssemblyReferences.Remove(y));
            }

            INFO("Writing output assembly to disk");
            var parameters = new WriterParameters();
            if (KeyFile != null && File.Exists(KeyFile))
            {
                using (var stream = new FileStream(KeyFile, FileMode.Open))
                {
                    parameters.StrongNameKeyPair = new System.Reflection.StrongNameKeyPair(stream);
                }
            }
            // write PDB/MDB?
            if (MergeDebugInfo)
                parameters.WriteSymbols = true;
            TargetAssemblyDefinition.Write(OutputFile, parameters);
            
            // TODO: we're done here, the code below is only test code which can be removed once it's all running fine
            // 'verify' generated assembly
            AssemblyDefinition asm2 = AssemblyDefinition.ReadAssembly(OutputFile, new ReaderParameters(ReadingMode.Immediate));
            // lazy match on the name (not full) to catch requirements about merging different versions
            bool failed = false;
            foreach (var a in asm2.MainModule.AssemblyReferences.Where(x => MergedAssemblies.Any(y => x.Name == y.Name.Name)))
            {
                // failed
                ERROR("Merged assembly still references " + a.FullName);
                failed = true;
            }
            if (failed)
                throw new Exception("Merging failed, see above errors");
        }

        public void ERROR(string msg)
        {
            Log("ERROR: " + msg);
        }

        public void INFO(string msg)
        {
            Log("INFO: " + msg);
        }

        public void VERBOSE(string msg)
        {
            Log("INFO: " + msg);
        }

        public void IGNOREDUP(string ignoredType, object ignoredObject)
        {
            // TODO: put on a list and log a summary
            INFO("Ignoring duplicate " + ignoredType + " " + ignoredObject);
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
                IGNOREDUP("field", field);
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

            CopyCustomAttributes(field.CustomAttributes, nf.CustomAttributes);
        }

        /// <summary>
        /// Clones a parameter into a newly created method
        /// </summary>
        private void CloneTo(ParameterDefinition param, MethodDefinition context, Collection<ParameterDefinition> col)
        {
            ParameterDefinition pd = new ParameterDefinition(param.Name, param.Attributes, Import(param.ParameterType, context));
            if (param.HasMarshalInfo)
                pd.MarshalInfo = param.MarshalInfo;
            col.Add(pd);
        }

        /// <summary>
        /// Clones a collection of SecurityDeclarations
        /// </summary>
        private void CopySecurityDeclarations(Collection<SecurityDeclaration> input, Collection<SecurityDeclaration> output, IGenericParameterProvider context)
        {
            foreach (SecurityDeclaration sec in input)
            {
                SecurityDeclaration newSec = new SecurityDeclaration(sec.Action);
                foreach (SecurityAttribute sa in sec.SecurityAttributes)
                {
                    newSec.SecurityAttributes.Add(new SecurityAttribute(Import(sa.AttributeType, context)));// TODO: Import AttributeType?
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

        private void CopyGenericParameters(Collection<GenericParameter> input, Collection<GenericParameter> output, IGenericParameterProvider nt)
        {
            foreach (GenericParameter gp in input)
            {
                GenericParameter ngp = new GenericParameter(gp.Name, nt);

                ngp.Attributes = gp.Attributes;
                output.Add(ngp);
            }
            // delay copy to ensure all generics parameters are already present
            Copy(input, output, (gp, ngp) => CopyTypeReferences(gp.Constraints, ngp.Constraints, nt));
            Copy(input, output, (gp, ngp) => CopyCustomAttributes(gp.CustomAttributes, ngp.CustomAttributes));
        }

        private void CloneTo(EventDefinition evt, TypeDefinition nt, Collection<EventDefinition> col)
        {
            // ignore duplicate event
            if (nt.Events.Any(x => x.Name == evt.Name))
            {
                IGNOREDUP("event", evt);
                return;
            }

            EventDefinition ed = new EventDefinition(evt.Name, evt.Attributes, Import(evt.EventType, nt));
            col.Add(ed);
            if (evt.AddMethod != null)
                ed.AddMethod = nt.Methods.Where(x => x.FullName == evt.AddMethod.FullName).First();
            if (evt.RemoveMethod != null)
                ed.RemoveMethod = nt.Methods.Where(x => x.FullName == evt.RemoveMethod.FullName).First();
            if (evt.InvokeMethod != null)
                ed.InvokeMethod = nt.Methods.Where(x => x.FullName == evt.InvokeMethod.FullName).First();
            if (evt.HasOtherMethods)
            {
                // TODO
                throw new InvalidOperationException();
            }

            CopyCustomAttributes(evt.CustomAttributes, ed.CustomAttributes);
        }

        private void CopyCustomAttributes(Collection<CustomAttribute> input, Collection<CustomAttribute> output)
        {
            foreach (CustomAttribute ca in input)
            {
                output.Add(new CustomAttribute(Import(ca.Constructor), ca.GetBlob()));
            }
        }

        private void CopyTypeReferences(Collection<TypeReference> input, Collection<TypeReference> output, IGenericParameterProvider context)
        {
            foreach (TypeReference ta in input)
            {
                output.Add(Import(ta, context));
            }
        }

        private TypeReference Import(TypeReference reference, IGenericParameterProvider context)
        {
            TypeDefinition type = MainModule.GetType(reference.FullName);
            if (type != null)
                return type;

            if (context is MethodReference)
                return MainModule.Import(reference, (MethodReference)context);
            if (context is TypeReference)
                return MainModule.Import(reference, (TypeReference)context);
            throw new InvalidOperationException();
        }

        private FieldReference Import(FieldReference reference, IGenericParameterProvider context)
        {
            if (context is MethodReference)
                return MainModule.Import(reference, (MethodReference)context);
            if (context is TypeReference)
                return MainModule.Import(reference, (TypeReference)context);
            throw new InvalidOperationException();
        }

        private MethodReference Import(MethodReference reference)
        {
            return MainModule.Import(reference);
        }

        private MethodReference Import(MethodReference reference, IGenericParameterProvider context)
        {
            // If this is a Method/TypeDefinition, it will be corrected to a definition again later

            if (context is MethodReference)
                return MainModule.Import(reference, (MethodReference)context);
            if (context is TypeReference)
                return MainModule.Import(reference, (TypeReference)context);
            throw new InvalidOperationException();
        }

        private void CloneTo(PropertyDefinition prop, TypeDefinition nt, Collection<PropertyDefinition> col)
        {
            // ignore duplicate property
            if (nt.Properties.Any(x => x.Name == prop.Name))
            {
                IGNOREDUP("property", prop);
                return;
            }

            PropertyDefinition pd = new PropertyDefinition(prop.Name, prop.Attributes, Import(prop.PropertyType, nt));
            col.Add(pd);
            if (prop.SetMethod != null)
                pd.SetMethod = nt.Methods.Where(x => x.FullName == prop.SetMethod.FullName).First();
            if (prop.GetMethod != null)
                pd.GetMethod = nt.Methods.Where(x => x.FullName == prop.GetMethod.FullName).First();
            if (prop.HasOtherMethods)
            {
                // TODO
                throw new NotSupportedException("Property has other methods");
            }

            CopyCustomAttributes(prop.CustomAttributes, pd.CustomAttributes);
        }

        private void CloneTo(MethodDefinition meth, TypeDefinition type)
        {
            // ignore duplicate method
            if (type.Methods.Any(x => (x.Name == meth.Name) && (x.Parameters.Count == meth.Parameters.Count) &&
                (x.ToString() == meth.ToString()))) // TODO: better/faster comparation of parameter types?
            {
                IGNOREDUP("method", meth);
                return;
            }
            // use void placeholder as we'll do the return type import later on (after generic parameters)
            MethodDefinition nm = new MethodDefinition(meth.Name, meth.Attributes, MainModule.TypeSystem.Void);
            nm.ImplAttributes = meth.ImplAttributes;

            type.Methods.Add(nm);

            CopyGenericParameters(meth.GenericParameters, nm.GenericParameters, nm);

            if (meth.HasPInvokeInfo)
                nm.PInvokeInfo = meth.PInvokeInfo;

            foreach (ParameterDefinition param in meth.Parameters)
                CloneTo(param, nm, nm.Parameters);

            foreach (MethodReference ov in meth.Overrides)
                nm.Overrides.Add(Import(ov, type));

            CopySecurityDeclarations(meth.SecurityDeclarations, nm.SecurityDeclarations, type);
            CopyCustomAttributes(meth.CustomAttributes, nm.CustomAttributes);

            nm.ReturnType = Import(meth.ReturnType, nm);
            if (meth.HasBody)
                CloneTo(meth.Body, nm);

            nm.IsAddOn = meth.IsAddOn;
            nm.IsRemoveOn = meth.IsRemoveOn;
            nm.IsGetter = meth.IsGetter;
            nm.IsSetter = meth.IsSetter;
            nm.CallingConvention = meth.CallingConvention;
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

            foreach (Instruction instr in body.Instructions)
            {
                Instruction ni;

                if (instr.OpCode.Code == Code.Calli)
                {
                    ni = Instruction.Create(instr.OpCode, (CallSite)instr.Operand);
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
                        ni = Instruction.Create(instr.OpCode, (Instruction)instr.Operand); // TODO review
                        break;
                    case OperandType.InlineSwitch:
                        ni = Instruction.Create(instr.OpCode, (Instruction[])instr.Operand); // TODO review
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
                if (instr.OpCode.OperandType != OperandType.ShortInlineBrTarget &&
                    instr.OpCode.OperandType != OperandType.InlineBrTarget)
                    continue;

                instr.Operand = GetInstruction(body, nb, (Instruction)body.Instructions[i].Operand);
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
                        neh.FilterEnd = GetInstruction(body, nb, eh.FilterEnd);
                        break;
                }

                nb.ExceptionHandlers.Add(neh);
            }
        }

        internal static Instruction GetInstruction(MethodBody oldBody, MethodBody newBody, Instruction i)
        {
            int pos = oldBody.Instructions.IndexOf(i);
            if (pos > -1 && pos < newBody.Instructions.Count)
                return newBody.Instructions[pos];

            return null /*newBody.Instructions.Outside*/;
        }

        internal void Import(TypeDefinition type, Collection<TypeDefinition> col)
        {
            TypeDefinition nt = MainModule.GetType(type.FullName);
            if (nt == null)
            {
                nt = new TypeDefinition(type.Namespace, type.Name, type.Attributes);
                col.Add(nt);

                CopyGenericParameters(type.GenericParameters, nt.GenericParameters, nt);
                if (type.BaseType != null)
                    nt.BaseType = Import(type.BaseType, nt);

                if (type.HasLayoutInfo)
                {
                    nt.ClassSize = type.ClassSize;
                    nt.PackingSize = type.PackingSize;
                }
            }
            else if (UnionMerge)
            {
                INFO("Merging " + type);
            }
            else
            {
                ERROR("Duplicate type " + type);
                throw new InvalidOperationException("Duplicate type " + type);
            }

            // nested types first
            foreach (TypeDefinition nested in type.NestedTypes)
                Import(nested, nt.NestedTypes);
            foreach (FieldDefinition field in type.Fields)
                CloneTo(field, nt);

            // methods before fields / events
            foreach (MethodDefinition meth in type.Methods)
                CloneTo(meth, nt);

            foreach (EventDefinition evt in type.Events)
                CloneTo(evt, nt, nt.Events);
            foreach (PropertyDefinition prop in type.Properties)
                CloneTo(prop, nt, nt.Properties);

            CopySecurityDeclarations(type.SecurityDeclarations, nt.SecurityDeclarations, nt);
            CopyTypeReferences(type.Interfaces, nt.Interfaces, nt);
            CopyCustomAttributes(type.CustomAttributes, nt.CustomAttributes);
        }
    }
}
