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
            _logger.INFO("Processing resources");
            // merge resources
            List<string> repackList = null;
            EmbeddedResource repackListRes = null;
            Dictionary<string, List<int>> ikvmExportsLists = null;
            EmbeddedResource ikvmExports = null;
            if (!_options.NoRepackRes)
            {
                repackList = _repackContext.MergedAssemblies.Select(a => a.FullName).ToList();
                repackListRes = GetRepackListResource(repackList);
                _targetAssemblyMainModule.Resources.Add(repackListRes);
            }

            foreach (var assembly in _repackContext.MergedAssemblies)
            {
                foreach (var r in assembly.Modules.SelectMany(x => x.Resources))
                {
                    if (r.Name == "ILRepack.List")
                    {
                        if (!_options.NoRepackRes && r is EmbeddedResource)
                        {
                            MergeRepackListResource(ref repackList, ref repackListRes, (EmbeddedResource)r);
                        }
                    }
                    else if (r.Name == "ikvm.exports")
                    {
                        if (r is EmbeddedResource)
                        {
                            MergeIkvmExportsResource(ref ikvmExportsLists, ref ikvmExports, (EmbeddedResource)r);
                        }
                    }
                    else
                    {
                        if (!_options.AllowDuplicateResources && _targetAssemblyMainModule.Resources.Any(x => x.Name == r.Name))
                        {
                            // Not much we can do about 'ikvm__META-INF!MANIFEST.MF'
                            _logger.WARN("Ignoring duplicate resource " + r.Name);
                        }
                        else
                        {
                            _logger.VERBOSE("- Importing " + r.Name);
                            var nr = r;
                            switch (r.ResourceType)
                            {
                                case ResourceType.AssemblyLinked:
                                    // TODO
                                    _logger.WARN("AssemblyLinkedResource reference may need to be fixed (to link to newly created assembly)" + r.Name);
                                    break;
                                case ResourceType.Linked:
                                    // TODO ? (or not)
                                    break;
                                case ResourceType.Embedded:
                                    var er = (EmbeddedResource)r;
                                    if (er.Name.EndsWith(".resources"))
                                    {
                                        nr = FixResxResource(er, assembly == _repackContext.PrimaryAssemblyDefinition);
                                    }
                                    break;
                            }
                            _targetAssemblyMainModule.Resources.Add(nr);
                        }
                    }
                }
            }
        }

        private void MergeRepackListResource(ref List<string> repackList, ref EmbeddedResource repackListRes, EmbeddedResource r)
        {
            var others = (string[])new BinaryFormatter().Deserialize(r.GetResourceStream());
            repackList = repackList.Union(others).ToList();
            EmbeddedResource repackListRes2 = GetRepackListResource(repackList);
            _targetAssemblyMainModule.Resources.Remove(repackListRes);
            _targetAssemblyMainModule.Resources.Add(repackListRes2);
            repackListRes = repackListRes2;
        }

        private void MergeIkvmExportsResource(ref Dictionary<string, List<int>> lists, ref EmbeddedResource existing, EmbeddedResource extra)
        {
            if (existing == null)
            {
                lists = ExtractIkvmExportsLists(extra);
                _targetAssemblyMainModule.Resources.Add(existing = extra);
            }
            else
            {
                _targetAssemblyMainModule.Resources.Remove(existing);
                var lists2 = ExtractIkvmExportsLists(extra);
                foreach (KeyValuePair<string, List<int>> kv in lists2)
                {
                    List<int> v;
                    if (!lists.TryGetValue(kv.Key, out v))
                    {
                        lists.Add(kv.Key, kv.Value);
                    }
                    else if (v != null)
                    {
                        if (kv.Value == null) // wildcard export
                            lists[kv.Key] = null;
                        else
                            lists[kv.Key] = v.Union(kv.Value).ToList();
                    }
                }
                existing = GenerateIkvmExports(lists);
                _targetAssemblyMainModule.Resources.Add(existing);
            }
        }

        private static Dictionary<string, List<int>> ExtractIkvmExportsLists(EmbeddedResource extra)
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

        private Resource FixResxResource(EmbeddedResource er, bool patchBaml)
        {
            MemoryStream stream = (MemoryStream)er.GetResourceStream();
            var output = new MemoryStream((int)stream.Length);
            var rw = new ResourceWriter(output);
            using (var rr = new ResReader(stream))
            {
                foreach (var res in rr)
                {
                    _logger.VERBOSE(string.Format("- Resource '{0}' (type: {1})", res.name, res.type));

                    if (res.type == "ResourceTypeCode.String" || res.type.StartsWith("System.String"))
                    {
                        string content = (string)rr.GetObject(res);
                        content = _repackContext.FixStr(content);
                        rw.AddResource(res.name, content);
                    }
                    else if (patchBaml && res.type == "ResourceTypeCode.Stream" && res.name.EndsWith(".baml"))
                    {
                        var bamlResourceProcessor = new BamlResourceProcessor(
                            _repackContext.PrimaryAssemblyDefinition, _repackContext.MergedAssemblies, res);
                        rw.AddResourceData(res.name, res.type, bamlResourceProcessor.GetProcessedResource());
                    }
                    else
                    {
                        string fix = _repackContext.FixStr(res.type);
                        if (fix == res.type)
                        {
                            rw.AddResourceData(res.name, res.type, res.data);
                        }
                        else
                        {
                            var output2 = new MemoryStream(res.data.Length);
                            var sr = new SerReader(_repackContext, new MemoryStream(res.data), output2);
                            sr.Stream();
                            rw.AddResourceData(res.name, fix, output2.ToArray());
                        }
                    }
                }
            }
            rw.Generate();
            output.Position = 0;
            return new EmbeddedResource(er.Name, er.Attributes, output);
        }

        private EmbeddedResource GetRepackListResource(List<string> repackList)
        {
            repackList.Sort();
            using (var stream = new MemoryStream())
            {
                new BinaryFormatter().Serialize(stream, repackList.ToArray());
                return new EmbeddedResource("ILRepack.List", ManifestResourceAttributes.Public, stream.ToArray());
            }
        }
    }
}
