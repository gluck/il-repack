using Mono.Linker.Steps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Linker;

namespace ILRepacking.Steps.Linker
{
    class RepackMarkStep : BaseStep
    {
        private RepackOptions _repackOptions;

        public RepackMarkStep(RepackOptions _repackOptions)
        {
            this._repackOptions = _repackOptions;
        }

        protected override void ProcessAssembly(AssemblyDefinition targetAssemblyDefinition)
        {
            Annotations.SetAction (targetAssemblyDefinition, AssemblyAction.Link);

            Annotations.Push(targetAssemblyDefinition);
            Annotations.Mark(targetAssemblyDefinition.MainModule);
            if (targetAssemblyDefinition.EntryPoint != null)
            {
                // for executable we only mark the entry point
                Annotations.Mark(targetAssemblyDefinition.EntryPoint.DeclaringType);
                ResolveFromAssemblyStep.MarkMethod(Context, targetAssemblyDefinition.EntryPoint, MethodAction.Parse);
            }
            if (_repackOptions.MarkPublic)
            {
                foreach (TypeDefinition type in targetAssemblyDefinition.MainModule.Types.Where(t => t.IsPublic))
                    ResolveFromAssemblyStep.MarkType(Context, type);

            }
            Annotations.Pop();
         }
    }
}
