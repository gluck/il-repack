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
        public bool Log { get; set; }
        public Version Version { get; set; }
        public string KeyFile { get; set; }

        [Obsolete("Not implemented yet")]
        public string LogFile { get; set; }

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
            Log = true;
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

        [STAThread]
        public static int Main(string[] args)
        {
            // TODO complete
            ILRepack repack = new ILRepack();
            try
            {
                repack.KeyFile = OptS(args.FirstOrDefault(x => x.StartsWith("/keyfile:")));
                repack.Log = OptB(args.FirstOrDefault(x => x.StartsWith("/log:")), repack.Log);
                repack.OutputFile = OptS(args.FirstOrDefault(x => x.StartsWith("/out:")));
                repack.UnionMerge = args.Any(x => x == "/union");
                if (args.Any(x => x.StartsWith("/ver:")))
                {
                    repack.Version = new Version(OptS(args.First(x => x.StartsWith("/ver:"))));
                }
                repack.SetInputAssemblies(args.Where(File.Exists).ToArray());
                repack.Repack();
            }
            catch (Exception e)
            {
                if (repack.Log) Console.WriteLine(e);
                return 1;
            }
            return 0;
        }


        public void SetInputAssemblies(string[] assems)
        {
            MergedAssemblies = assems.Select(x => AssemblyDefinition.ReadAssembly(x)).ToList();
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
            if (TargetKind.HasValue) switch (TargetKind.Value)
            {
                case Kind.Dll:    kind = ModuleKind.Dll;     break;
                case Kind.Exe:    kind = ModuleKind.Console; break;
                case Kind.WinExe: kind = ModuleKind.Windows; break;
            }
            TargetAssemblyDefinition = AssemblyDefinition.CreateAssembly(OrigMainAssemblyDefinition.Name, OrigMainModule.Name,
                new ModuleParameters()
                    {
                        Kind = kind,
                        Architecture = OrigMainModule.Architecture,
                        Runtime = OrigMainModule.Runtime
                    });
            if (Version != null) TargetAssemblyDefinition.Name.Version = Version;

            INFO("Processing references");
            // Add all references to merged assembly (probably not necessary)
            foreach (var z in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.AssemblyReferences))
            {
                string name = z.Name;
                if (!MergedAssemblies.Any(y => y.Name.Name == name) &&
                    TargetAssemblyDefinition.Name.Name != name &&
                    !TargetAssemblyDefinition.MainModule.AssemblyReferences.Any(y => y.Name == name))
                {
                    INFO("- add reference " + z);
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
                INFO("- Importing " + r.Name);
                TargetAssemblyDefinition.MainModule.Resources.Add(r);
            }

            INFO("Processing types");
            // merge types
            foreach (var r in MergedAssemblies.SelectMany(x => x.Modules).SelectMany(x => x.Types))
            {
                if (r.FullName == "<Module>") continue;
                INFO("- Importing " + r);
                Import(r, MainModule.Types);
            }

            CopyCustomAttributes(OrigMainAssemblyDefinition.CustomAttributes, TargetAssemblyDefinition.CustomAttributes);
            CopySecurityDeclarations(OrigMainAssemblyDefinition.SecurityDeclarations, TargetAssemblyDefinition.SecurityDeclarations);

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

            var parameters = new WriterParameters();
            if (KeyFile != null && File.Exists(KeyFile))
            {
                using (var stream = new FileStream(KeyFile, FileMode.Open))
                {
                    parameters.StrongNameKeyPair = new System.Reflection.StrongNameKeyPair(stream);
                }
            }
            TargetAssemblyDefinition.Write(OutputFile, parameters);

            // 'verify' generated assembly
            AssemblyDefinition asm2 = AssemblyDefinition.ReadAssembly(OutputFile);
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
            if (Log) Console.WriteLine("ERROR: " + msg);
        }

        public void INFO(string msg)
        {
            if (Log) Console.WriteLine("INFO: " + msg);
        }

        // Real stuff below //

        // These methods are somehow a merge between the clone methods of Cecil 0.6 and the import ones of 0.9
        // They use Cecil's MetaDataImporter to rebase imported stuff into the new assembly, but then another pass is required
        //  to clean the TypeRefs Cecil keeps around (although the generated IL would be kind-o valid without, whatever 'valid' means)

        private void CloneTo(FieldDefinition field, TypeDefinition nt)
        {
            if (nt.Fields.Any(x => x.Name == field.Name))
            {
                Console.WriteLine("Ignoring duplicate field " + field);
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

        private void CloneTo(ParameterDefinition param, MethodDefinition context, Collection<ParameterDefinition> col)
        {
            ParameterDefinition pd = new ParameterDefinition(param.Name, param.Attributes, Import(param.ParameterType, context));
            col.Add(pd);
        }

        private void CopySecurityDeclarations(Collection<SecurityDeclaration> input, Collection<SecurityDeclaration> output)
        {
            foreach (SecurityDeclaration sec in input)
            {
                output.Add(new SecurityDeclaration(sec.Action));
            }
        }

        private void CopyGenericParameters(Collection<GenericParameter> input, Collection<GenericParameter> output, IGenericParameterProvider nt)
        {
            foreach (GenericParameter gp in input)
            {
                GenericParameter ngp = new GenericParameter(gp.Name, nt);

                ngp.Attributes = gp.Attributes;
                output.Add(ngp);

                CopyTypeReferences(gp.Constraints, ngp.Constraints, nt);
                CopyCustomAttributes(gp.CustomAttributes, ngp.CustomAttributes);
            }
        }

        private void CloneTo(EventDefinition evt, TypeDefinition nt, Collection<EventDefinition> col)
        {
            // TODO: ignore duplicate event
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
            if (type != null) return type;

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
            if (context is MethodReference)
                return MainModule.Import(reference, (MethodReference)context);
            if (context is TypeReference)
                return MainModule.Import(reference, (TypeReference)context);
            throw new InvalidOperationException();
        }

        private void CloneTo(PropertyDefinition prop, TypeDefinition nt, Collection<PropertyDefinition> col)
        {
            // TODO: ignore duplicate property
            PropertyDefinition pd = new PropertyDefinition(prop.Name, prop.Attributes, Import(prop.PropertyType, nt));
            col.Add(pd);
            if (prop.SetMethod != null)
                pd.SetMethod = nt.Methods.Where(x => x.FullName == prop.SetMethod.FullName).First();
            if (prop.GetMethod != null)
                pd.GetMethod = nt.Methods.Where(x => x.FullName == prop.GetMethod.FullName).First();
            if (prop.HasOtherMethods)
            {
                // TODO
                throw new InvalidOperationException();
            }

            CopyCustomAttributes(prop.CustomAttributes, pd.CustomAttributes);
        }

        private void CloneTo(MethodDefinition meth, TypeDefinition type)
        {
            // TODO: ignore duplicate method
            MethodDefinition nm = new MethodDefinition(meth.Name, meth.Attributes, null);
            nm.ImplAttributes = meth.ImplAttributes;

            type.Methods.Add(nm);

            CopyGenericParameters(meth.GenericParameters, nm.GenericParameters, nm);

            if (meth.HasPInvokeInfo)
                nm.PInvokeInfo = meth.PInvokeInfo;

            foreach (ParameterDefinition param in meth.Parameters)
                CloneTo(param, nm, nm.Parameters);

            foreach (MethodReference ov in meth.Overrides)
                nm.Overrides.Add(Import(ov, type));

            CopySecurityDeclarations(meth.SecurityDeclarations, nm.SecurityDeclarations);
            CopyCustomAttributes(meth.CustomAttributes, nm.CustomAttributes);

            nm.ReturnType = Import(meth.ReturnType, nm);
            if (meth.HasBody)
                CloneTo(meth.Body, nm);
        }

        private void CloneTo(MethodBody body, MethodDefinition parent)
        {
            MethodBody nb = new MethodBody(parent);
            parent.Body = nb;

            nb.MaxStackSize = body.MaxStackSize;
            nb.InitLocals = body.InitLocals;
            nb.LocalVarToken = body.LocalVarToken;
            //nb.CodeSize = body.CodeSize;

            foreach (VariableDefinition var in body.Variables)
                nb.Variables.Add(new VariableDefinition(
                    Import(var.VariableType, parent)));

            foreach (Instruction instr in body.Instructions)
            {
                Instruction ni;

			    if (instr.OpCode.Code == Code.Calli)
			    {
			        ni = Instruction.Create(instr.OpCode, (CallSite) instr.Operand);
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
            TypeDefinition nt = MainModule.GetType(type.Namespace, type.Name);
            if (nt == null)
            {
                nt = new TypeDefinition(type.Namespace, type.Name, type.Attributes);
                col.Add(nt);

                CopyGenericParameters(type.GenericParameters, nt.GenericParameters, nt);

                if (type.BaseType != null) nt.BaseType = Import(type.BaseType, nt);

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
            foreach (TypeDefinition nested in type.NestedTypes) Import(nested, nt.NestedTypes);
            foreach (FieldDefinition field in type.Fields) CloneTo(field, nt);

            // methods before fields / events
            foreach (MethodDefinition meth in type.Methods) CloneTo(meth, nt);

            foreach (EventDefinition evt in type.Events) CloneTo(evt, nt, nt.Events);
            foreach (PropertyDefinition prop in type.Properties) CloneTo(prop, nt, nt.Properties);

            CopySecurityDeclarations(type.SecurityDeclarations, nt.SecurityDeclarations);
            CopyTypeReferences(type.Interfaces, nt.Interfaces, nt);
            CopyCustomAttributes(type.CustomAttributes, nt.CustomAttributes);
        }

    }
}
