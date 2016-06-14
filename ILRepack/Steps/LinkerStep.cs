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

        public LinkerStep(IRepackContext repackContext, RepackOptions repackOptions)
        {
            _repackOptions = repackOptions;
            _repackContext = repackContext;
        }

        public void Perform()
        {
            if (!_repackOptions.Link)
                return;
            Pipeline p = new Pipeline();
            p.AppendStep(new Linker.BlacklistStep());
            p.AppendStep(new TypeMapStep());
            p.AppendStep(new RepackMarkStep(_repackOptions));
            p.AppendStep(new SubStepDispatcher() { new ApplyPreserveAttribute() });
            p.AppendStep(new MarkStep());
            p.AppendStep(new SweepStep());
            p.AppendStep(new CleanStep());
            LinkContext context = new LinkContext(p, _repackContext.GlobalAssemblyResolver) {
                LogInternalExceptions = true
            };
            _repackContext.GlobalAssemblyResolver.TargetRepackAssembly = _repackContext.TargetAssemblyDefinition;
           p.Process(context);
        }
    }
}
