using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ILRepacking
{
    /// <summary>
    /// This feature, when enabled, allows to store debug indexes within the assembly itself.
    /// It was inspired by IKVM (which does it for Java assemblies), and re-uses the same attributes.
    /// It then allows at runtime to display file:line information on all stacktraces, by resolving the IL offset provided.
    /// </summary>
    internal class IKVMLineIndexer
    {
        private readonly IRepackContext repack;
        private bool enabled;
        private string fileName;
        private TypeReference sourceFileAttributeTypeReference;
        private TypeReference lineNumberTableAttributeTypeReference;
        private MethodReference lineNumberTableAttributeConstructor1;
        private MethodReference lineNumberTableAttributeConstructor2;
        private MethodReference sourceFileAttributeConstructor;

        protected ModuleDefinition TargetAssemblyMainModule
        {
            get
            {
                return repack.TargetAssemblyMainModule;
            }
        }

        public IKVMLineIndexer(IRepackContext ilRepack, bool doLineIndexing)
        {
            repack = ilRepack;
            enabled = doLineIndexing;
        }

        public void Reset()
        {
            fileName = null;
        }

        public void PreMethodBodyRepack(MethodBody body, MethodDefinition parent)
        {
            if (!enabled || !parent.DebugInformation.HasSequencePoints)
                return;

            Reset();
            if (!parent.CustomAttributes.Any(x => x.Constructor.DeclaringType.Name == "LineNumberTableAttribute"))
            {
                var lineNumberWriter = new LineNumberWriter(body.Instructions.Count / 4);
                foreach (var sp in parent.DebugInformation.SequencePoints)
                {
                    AddSeqPoint(sp, lineNumberWriter);
                }
                PostMethodBodyRepack(parent, lineNumberWriter);
            }
        }

        private void AddSeqPoint(SequencePoint currentSeqPoint, LineNumberWriter lineNumberWriter) 
        {
            if (currentSeqPoint != null)
            {
                if (fileName == null && currentSeqPoint.Document != null)
                {
                    var url = currentSeqPoint.Document.Url;
                    if (url != null)
                    {
                        try
                        {
                            fileName = new FileInfo(url).Name;
                        }
                        catch
                        {
                            // for mono
                        }
                    }
                }
                if (currentSeqPoint.StartLine == 0xFeeFee && currentSeqPoint.EndLine == 0xFeeFee)
                {
                    if (lineNumberWriter.LineNo > 0)
                    {
                        lineNumberWriter.AddMapping(currentSeqPoint.Offset, -1);
                    }
                }
                else
                {
                    if (lineNumberWriter.LineNo != currentSeqPoint.StartLine)
                    {
                        lineNumberWriter.AddMapping(currentSeqPoint.Offset, currentSeqPoint.StartLine);
                    }
                }
            }
        }

        private void PostMethodBodyRepack(MethodDefinition parent, LineNumberWriter lineNumberWriter)
        {
            if (lineNumberWriter.Count > 0)
            {
                CustomAttribute ca;
                if (lineNumberWriter.Count == 1)
                {
                    ca =
                        new CustomAttribute(lineNumberTableAttributeConstructor1)
                        {
                            ConstructorArguments = { new CustomAttributeArgument(TargetAssemblyMainModule.TypeSystem.UInt16, (ushort)lineNumberWriter.LineNo) }
                        };
                }
                else
                {
                    ca =
                        new CustomAttribute(lineNumberTableAttributeConstructor2)
                        {
                            ConstructorArguments = { new CustomAttributeArgument(new ArrayType(TargetAssemblyMainModule.TypeSystem.Byte), lineNumberWriter.ToArray().Select(b => new CustomAttributeArgument(TargetAssemblyMainModule.TypeSystem.Byte, b)).ToArray()) }
                        };
                }
                parent.CustomAttributes.Add(ca);
                if (fileName != null)
                {
                    var type = parent.DeclaringType;
                    var exist = type.CustomAttributes.FirstOrDefault(x => x.Constructor.DeclaringType.Name == "SourceFileAttribute");
                    if (exist == null)
                    {
                        // put the filename on the type first
                        type.CustomAttributes.Add(new CustomAttribute(sourceFileAttributeConstructor)
                        {
                            ConstructorArguments = { new CustomAttributeArgument(TargetAssemblyMainModule.TypeSystem.String, fileName) }
                        });
                    }
                    else if (fileName != (string)exist.ConstructorArguments[0].Value)
                    {
                        // if already specified on the type, but different (e.g. for partial classes), put the attribute on the method.
                        // Note: attribute isn't allowed for Methods, but that restriction doesn't apply to IL generation (or runtime use)
                        parent.CustomAttributes.Add(new CustomAttribute(sourceFileAttributeConstructor)
                        {
                            ConstructorArguments = { new CustomAttributeArgument(TargetAssemblyMainModule.TypeSystem.String, fileName) }
                        });
                    }
                }
            }
        }

        public void PostRepackReferences()
        {
            if (!enabled)
                return;

            IMetadataScope ikvmRuntimeReference = repack.TargetAssemblyMainModule.AssemblyReferences.FirstOrDefault(r => r.Name == "IKVM.Runtime");
            if (ikvmRuntimeReference == null)
            {
                ikvmRuntimeReference = repack.MergeScope(repack.GlobalAssemblyResolver.Resolve(new AssemblyNameReference("IKVM.Runtime", null)).Name);
            }
            if (ikvmRuntimeReference == null)
            {
                enabled = false;
            }
            else
            {
                sourceFileAttributeTypeReference = new TypeReference("IKVM.Attributes", "SourceFileAttribute", TargetAssemblyMainModule, ikvmRuntimeReference);
                sourceFileAttributeConstructor = new MethodReference(".ctor", TargetAssemblyMainModule.TypeSystem.Void, sourceFileAttributeTypeReference)
                                                     {HasThis = true, Parameters = {new ParameterDefinition(TargetAssemblyMainModule.TypeSystem.String)}};

                lineNumberTableAttributeTypeReference = new TypeReference("IKVM.Attributes", "LineNumberTableAttribute", TargetAssemblyMainModule, ikvmRuntimeReference);
                lineNumberTableAttributeConstructor1 = new MethodReference(".ctor", TargetAssemblyMainModule.TypeSystem.Void, lineNumberTableAttributeTypeReference)
                                                           {HasThis = true, Parameters = {new ParameterDefinition(TargetAssemblyMainModule.TypeSystem.UInt16)}};
                lineNumberTableAttributeConstructor2 = new MethodReference(".ctor", TargetAssemblyMainModule.TypeSystem.Void, lineNumberTableAttributeTypeReference)
                                                           {HasThis = true, Parameters = {new ParameterDefinition(new ArrayType(TargetAssemblyMainModule.TypeSystem.Byte))}};
            }
        }
    }
}
