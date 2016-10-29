//
// Copyright (c) 2011 Francois Valdy
// Copyright (c) 2015 Timotei Dolean
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//

using ILRepacking.Steps.ResourceProcessing;
using Mono.Cecil;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;
using System.Runtime.Serialization.Formatters.Binary;

namespace ILRepacking.Steps
{
    internal class ResourcesRepackStep : IRepackStep
    {
        internal const string ILRepackListResourceName = "ILRepack.List";
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly RepackOptions _options;
        private readonly ModuleDefinition _targetAssemblyMainModule;

        public ResourcesRepackStep(
            ILogger logger,
            IRepackContext repackContext,
            RepackOptions options)
        {
            _logger = logger;
            _options = options;
            _repackContext = repackContext;
            _targetAssemblyMainModule = _repackContext.TargetAssemblyMainModule;
        }

        public void Perform()
        {
            _logger.Info("Processing resources");
            // merge resources
            IEnumerable<string> repackList = new List<string>();
            Dictionary<string, List<int>> ikvmExportsLists = new Dictionary<string, List<int>>();
            if (!_options.NoRepackRes)
            {
                repackList = _repackContext.MergedAssemblies.Select(a => a.FullName).ToList();
            }

            var bamlStreamCollector = new BamlStreamCollector(_logger, _repackContext);
            var bamlResourcePatcher = new BamlResourcePatcher(_repackContext);

            var primaryAssemblyProcessors =
                new[] { bamlResourcePatcher }.Union(GetCommonResourceProcessors()).ToList();
            var otherAssemblyProcessors =
                new List<IResProcessor> { bamlResourcePatcher, bamlStreamCollector }.Union(GetCommonResourceProcessors()).ToList();

            // Primary Assembly *must* be the last one in order to properly gather the resources
            // from dependencies
            var assembliesList = _repackContext.OtherAssemblies.Concat(new[] { _repackContext.PrimaryAssemblyDefinition });
            foreach (var assembly in assembliesList)
            {
                bool isPrimaryAssembly = (assembly == _repackContext.PrimaryAssemblyDefinition);
                var assemblyProcessors = isPrimaryAssembly ? primaryAssemblyProcessors : otherAssemblyProcessors;

                foreach (var resource in assembly.Modules.SelectMany(x => x.Resources))
                {
                    if (resource.Name == ILRepackListResourceName)
                    {
                        if (!_options.NoRepackRes && resource is EmbeddedResource)
                        {
                            repackList = repackList.Union(GetRepackListFromResource((EmbeddedResource)resource));
                        }
                    }
                    else if (resource.Name == "ikvm.exports")
                    {
                        if (resource is EmbeddedResource)
                        {
                            ikvmExportsLists = MergeIkvmExports(
                                ikvmExportsLists,
                                GetIkvmExportsListsFromResource((EmbeddedResource)resource));
                        }
                    }
                    else
                    {
                        if (!_options.AllowDuplicateResources && _targetAssemblyMainModule.Resources.Any(x => x.Name == resource.Name))
                        {
                            // Not much we can do about 'ikvm__META-INF!MANIFEST.MF'
                            _logger.Warn("Ignoring duplicate resource " + resource.Name);
                        }
                        else
                        {
                            _logger.Verbose("- Importing " + resource.Name);
                            var newResource = resource;
                            switch (resource.ResourceType)
                            {
                                case ResourceType.AssemblyLinked:
                                    // TODO
                                    _logger.Warn("AssemblyLinkedResource reference may need to be fixed (to link to newly created assembly)" + resource.Name);
                                    break;
                                case ResourceType.Linked:
                                    // TODO ? (or not)
                                    break;
                                case ResourceType.Embedded:
                                    var er = (EmbeddedResource)resource;
                                    if (er.Name.EndsWith(".resources"))
                                    {
                                        // we don't want to write the bamls to other embedded resource files
                                        bool shouldWriteCollectedBamlStreams =
                                            isPrimaryAssembly &&
                                            $"{assembly.Name.Name}.g.resources".Equals(er.Name);

                                        newResource = FixResxResource(assembly, er, assemblyProcessors,
                                            shouldWriteCollectedBamlStreams ? bamlStreamCollector : null);
                                    }
                                    break;
                            }
                            _targetAssemblyMainModule.Resources.Add(newResource);
                        }
                    }
                }
            }

            if (ikvmExportsLists.Count > 0)
                _targetAssemblyMainModule.Resources.Add(GenerateIkvmExports(ikvmExportsLists));

            if (!_options.NoRepackRes)
                _targetAssemblyMainModule.Resources.Add(GenerateRepackListResource(repackList.ToList()));
        }

        private List<IResProcessor> GetCommonResourceProcessors()
        {
            return new List<IResProcessor>
            {
                new StringResourceProcessor(_repackContext),
                new GenericResourceProcessor(_repackContext)
            };
        }

        private static Dictionary<string, List<int>> MergeIkvmExports(
            Dictionary<string, List<int>> currentExports,
            Dictionary<string, List<int>> extraExports)
        {
            Dictionary<string, List<int>> result = new Dictionary<string, List<int>>(currentExports);

            foreach (var pair in extraExports)
            {
                List<int> values;
                if (!currentExports.TryGetValue(pair.Key, out values))
                {
                    currentExports.Add(pair.Key, pair.Value);
                }
                else if (values != null)
                {
                    if (pair.Value == null) // wildcard export
                        currentExports[pair.Key] = null;
                    else
                        currentExports[pair.Key] = values.Union(pair.Value).ToList();
                }
            }

            return result;
        }

        private static Dictionary<string, List<int>> GetIkvmExportsListsFromResource(EmbeddedResource extra)
        {
            Dictionary<string, List<int>> ikvmExportsLists = new Dictionary<string, List<int>>();
            BinaryReader rdr = new BinaryReader(extra.GetResourceStream());
            int assemblyCount = rdr.ReadInt32();
            for (int i = 0; i < assemblyCount; i++)
            {
                var str = rdr.ReadString();
                int typeCount = rdr.ReadInt32();
                if (typeCount == 0)
                {
                    ikvmExportsLists.Add(str, null);
                }
                else
                {
                    var types = new List<int>();
                    ikvmExportsLists.Add(str, types);
                    for (int j = 0; j < typeCount; j++)
                        types.Add(rdr.ReadInt32());
                }
            }
            return ikvmExportsLists;
        }

        private static EmbeddedResource GenerateIkvmExports(Dictionary<string, List<int>> lists)
        {
            using (var stream = new MemoryStream())
            {
                var bw = new BinaryWriter(stream);
                bw.Write(lists.Count);
                foreach (KeyValuePair<string, List<int>> kv in lists)
                {
                    bw.Write(kv.Key);
                    if (kv.Value == null)
                    {
                        // wildcard export
                        bw.Write(0);
                    }
                    else
                    {
                        bw.Write(kv.Value.Count);
                        foreach (int hash in kv.Value)
                        {
                            bw.Write(hash);
                        }
                    }
                }
                return new EmbeddedResource("ikvm.exports", ManifestResourceAttributes.Public, stream.ToArray());
            }
        }

        private Resource FixResxResource(
            AssemblyDefinition containingAssembly,
            EmbeddedResource er,
            List<IResProcessor> resourcePrcessors,
            IEmbeddedResourceProcessor embeddedResourceProcessor)
        {
            MemoryStream stream = (MemoryStream)er.GetResourceStream();
            var output = new MemoryStream((int)stream.Length);
            var rw = new ResourceWriter(output);

            using (var rr = new ResReader(stream))
            {
                foreach (var res in rr)
                {
                    foreach (var processor in resourcePrcessors)
                    {
                        if (processor.Process(containingAssembly, res, rr, rw))
                            break;
                    }
                }
            }

            // do a final processing, if any, on the embeddedResource itself
            embeddedResourceProcessor?.Process(er, rw);

            rw.Generate();
            output.Position = 0;
            return new EmbeddedResource(er.Name, er.Attributes, output);
        }

        private static string[] GetRepackListFromResource(EmbeddedResource resource)
        {
            return (string[])new BinaryFormatter().Deserialize(resource.GetResourceStream());
        }

        private static EmbeddedResource GenerateRepackListResource(List<string> repackList)
        {
            repackList.Sort();
            using (var stream = new MemoryStream())
            {
                new BinaryFormatter().Serialize(stream, repackList.ToArray());
                return new EmbeddedResource(ILRepackListResourceName, ManifestResourceAttributes.Public, stream.ToArray());
            }
        }
    }
}
