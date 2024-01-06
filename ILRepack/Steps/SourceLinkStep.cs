using System;
using System.Collections.Generic;
using System.Text;
using Mono.Cecil.Cil;

namespace ILRepacking.Steps
{
    internal class SourceLinkStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;

        public SourceLinkStep(ILogger logger, IRepackContext repackContext)
        {
            _logger = logger;
            _repackContext = repackContext;
        }

        private class SourceLinkData
        {
#pragma warning disable CS0649
            public Dictionary<string, string> documents;
#pragma warning restore CS0649
        }

        public void Perform()
        {
            var aggregated = new Dictionary<string, string>();

            foreach (var input in _repackContext.MergedAssemblies)
            {
                var module = input.MainModule;
                if (!module.HasCustomDebugInformations)
                {
                    continue;
                }

                foreach (var custom in module.CustomDebugInformations)
                {
                    if (custom is not SourceLinkDebugInformation sourceLinkDebugInformation)
                    {
                        continue;
                    }

                    var sourceLink = sourceLinkDebugInformation.Content;
                    if (string.IsNullOrWhiteSpace(sourceLink))
                    {
                        continue;
                    }

                    var data = TinyJson.JSONParser.FromJson<SourceLinkData>(sourceLink);
                    if (data == null || data.documents == null)
                    {
                        continue;
                    }

                    foreach (var kvp in data.documents)
                    {
                        aggregated[kvp.Key] = kvp.Value;
                    }
                }
            }

            if (aggregated.Count == 0)
            {
                return;
            }

            string finalJson = WriteJson(aggregated);
            var sourceLinkInformation = new SourceLinkDebugInformation(finalJson);

            _repackContext.TargetAssemblyMainModule.CustomDebugInformations.Add(sourceLinkInformation);

            _logger.Verbose($"Wrote aggregated SourceLink json with {aggregated.Count} entries");
        }

        private string WriteJson(Dictionary<string, string> dictionary)
        {
            var sb = new StringBuilder();
            sb.Append("{\"documents\":{");

            bool isFirst = true;

            foreach (var kvp in dictionary)
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    sb.Append(",");
                }

                sb.Append("\"");
                sb.Append(kvp.Key);
                sb.Append("\"");
                sb.Append(":");
                sb.Append("\"");
                sb.Append(kvp.Value);
                sb.Append("\"");
            }

            sb.Append("}}");

            var result = sb.ToString();
            return result;
        }
    }
}