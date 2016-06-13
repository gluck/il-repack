using ILRepacking.Steps.Linker;
using Mono.Cecil;
using Mono.Linker;
using Mono.Linker.Steps;
using Mono.Tuner;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ILRepacking.Steps
{
    internal class LinkerStep : IRepackStep
    {
        readonly IRepackContext _repackContext;
        readonly RepackOptions _repackOptions;

        public LinkerStep(
            IRepackContext repackContext,
            RepackOptions repackOptions)
        {
            _repackContext = repackContext;
            _repackOptions = repackOptions;
        }

        public void Perform()
        {
            Pipeline p = new Pipeline();
            p.AppendStep(new BlacklistStep());
            p.AppendStep(new TypeMapStep());
            p.AppendStep(new SubStepDispatcher() { new ApplyPreserveAttribute() });
            p.AppendStep(new MarkStep());
            p.AppendStep(new SweepStep());
            p.AppendStep(new CleanStep());
            LinkContext context = new LinkContext(p, _repackContext.GlobalAssemblyResolver);
            _repackContext.GlobalAssemblyResolver.TargetRepackAssembly = _repackContext.TargetAssemblyDefinition;
            //if (_repackContext.TargetAssemblyDefinition.EntryPoint != null)
            //{
            //    ResolveFromAssemblyStep.ProcessExecutable(context, _repackContext.TargetAssemblyDefinition);
            //}
            //else
            {
                context.Annotations.SetAction (_repackContext.TargetAssemblyDefinition, AssemblyAction.Link);

                context.Annotations.Push(_repackContext.TargetAssemblyDefinition);
                if (_repackContext.TargetAssemblyDefinition.EntryPoint != null)
                {
                    context.Annotations.Mark(_repackContext.TargetAssemblyDefinition.EntryPoint.DeclaringType);
                    ResolveFromAssemblyStep.MarkMethod(context, _repackContext.TargetAssemblyDefinition.EntryPoint, MethodAction.Parse);
                }

                context.Annotations.Mark(_repackContext.TargetAssemblyDefinition.MainModule);

                foreach (TypeDefinition type in _repackContext.TargetAssemblyDefinition.MainModule.Types.Where(t => t.IsPublic))
                    ResolveFromAssemblyStep.MarkType(context, type);

                context.Annotations.Pop();
            }
            p.Process(context);
        }
    }
}
