using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;

namespace ILRepacking.Steps.ResourceProcessing
{
    internal class NameSpaceRenameProcessor : IEmbeddedResourceProcessor
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly RepackOptions _repackOptions;

        public NameSpaceRenameProcessor(ILogger logger, IRepackContext repackContext, RepackOptions _repackOptions)
        {
            this._logger = logger;
            this._repackContext = repackContext;
            this._repackOptions = _repackOptions;
        }

        public void Process(EmbeddedResource embeddedResource, ResourceWriter resourceWriter)
        {
            if (!this._repackOptions.RenameNameSpaces)
                return;

            if (this._repackOptions.RenameNameSpacesMatches.Count == 0)
                return;

            foreach (var namespacesToReplace in this._repackOptions.RenameNameSpacesMatches)
            {
                if (namespacesToReplace.Key.IsMatch(embeddedResource.Name))
                {
                    embeddedResource.Name = namespacesToReplace.Key.Replace(embeddedResource.Name, namespacesToReplace.Value);

                    break;
                }
            }
        }
    }
}
