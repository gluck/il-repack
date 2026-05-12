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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;

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
            _logger.Verbose("Processing resources");

            // merge resources
            IEnumerable<string> repackList = new List<string>();
            Dictionary<string, List<int>> ikvmExportsLists = new Dictionary<string, List<int>>();
            if (!_options.NoRepackRes)
            {
                repackList = _repackContext.MergedAssemblies.Select(a => a.FullName).ToList();
            }

            bool areCollectedStreamsWritten = false;
            var bamlStreamCollector = new BamlStreamCollector(_logger, _repackContext);
            var bamlResourcePatcher = new BamlResourcePatcher(_repackContext);
            var commonProcessors = new List<IResProcessor>
            {
                new StringResourceProcessor(_repackContext),
                new GenericResourceProcessor(_repackContext)
            };

            var primaryAssemblyProcessors =
                new[] { bamlResourcePatcher }.Union(commonProcessors).ToList();
            var otherAssemblyProcessors =
                new List<IResProcessor> { bamlResourcePatcher, bamlStreamCollector }.Union(commonProcessors).ToList();

            // Primary Assembly *must* be the last one in order to properly gather the resources
            // from dependencies
            var assembliesList = _repackContext.OtherAssemblies.Concat(new[] { _repackContext.PrimaryAssemblyDefinition });
            foreach (var assembly in assembliesList)
            {
                bool isPrimaryAssembly = assembly == _repackContext.PrimaryAssemblyDefinition;
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
                        var isDuplicate = !_options.AllowDuplicateResources && _targetAssemblyMainModule.Resources.Any(x => x.Name == resource.Name);

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

                                    if (shouldWriteCollectedBamlStreams)
                                        areCollectedStreamsWritten = true;

                                    newResource = FixResxResource(assembly, er, assemblyProcessors,
                                        shouldWriteCollectedBamlStreams ? bamlStreamCollector : null);

                                    // .resources blobs are always merged regardless of AllowDuplicateResources —
                                    // ResourceManager locates a resource by exact name, so two blobs with the same
                                    // name in one assembly means the second is permanently unreachable.
                                    var existingResx = _targetAssemblyMainModule.Resources
                                        .FirstOrDefault(x => x.Name == resource.Name) as EmbeddedResource;
                                    if (existingResx != null)
                                    {
                                        _logger.Warn($"Duplicate .resources {resource.Name}, merging entries from {assembly.Name.Name}");
                                        newResource = MergeEmbeddedResxResources(existingResx, (EmbeddedResource)newResource);
                                        _targetAssemblyMainModule.Resources.Remove(existingResx);
                                        isDuplicate = false;
                                    }
                                }
                                break;
                        }

                        if (isDuplicate)
                        {
                            _logger.Warn($"Duplicate resource {resource.Name}, replacing with version from {assembly.Name.Name}");
                            _targetAssemblyMainModule.Resources.Remove(
                                _targetAssemblyMainModule.Resources.First(x => x.Name == resource.Name));
                        }
                        _targetAssemblyMainModule.Resources.Add(newResource);
                    }
                }
            }

            if (ikvmExportsLists.Count > 0)
                _targetAssemblyMainModule.Resources.Add(
                    GenerateIkvmExports(ikvmExportsLists));

            if (!_options.NoRepackRes)
                _targetAssemblyMainModule.Resources.Add(
                    GenerateRepackListResource(repackList));

            CreateNewBamlResourceIfNeeded(areCollectedStreamsWritten, bamlStreamCollector);
        }

        private void CreateNewBamlResourceIfNeeded(bool areCollectedStreamsWritten, BamlStreamCollector bamlStreamCollector)
        {
            // if there weren't any (BAML) resources in the original assembly, then we need to create a new resource
            if (areCollectedStreamsWritten || !bamlStreamCollector.HasBamlStreams)
                return;

            string resourceName = _repackContext.PrimaryAssemblyDefinition.Name.Name + ".g.resources";
            EmbeddedResource resource = new EmbeddedResource(resourceName, ManifestResourceAttributes.Public, new byte[0]);
            var output = new MemoryStream();
            var rw = new ResourceWriter(output);

            // do a final processing, if any, on the embeddedResource itself
            bamlStreamCollector.Process(resource, rw);

            rw.Generate();
            output.Position = 0;
            _targetAssemblyMainModule.Resources.Add(
                new EmbeddedResource(resource.Name, resource.Attributes, output));
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
            var resourceBytes = stream.ToArray();
            string readerType;

            using (var rr = new ResReader(stream))
            {
                readerType = rr.ReaderType;
                foreach (var res in rr)
                {
                    foreach (var processor in resourcePrcessors)
                    {
                        if (processor.Process(res, containingAssembly, er, rr, rw))
                            break;
                    }
                }
            }

            // do a final processing, if any, on the embeddedResource itself
            embeddedResourceProcessor?.Process(er, rw);

            rw.Generate();
            output.Position = 0;

            if (readerType.StartsWith("System.Resources.Extensions.DeserializingResourceReader"))
            {
                // Bugfix#277 
                // Default ResourceWriter creates incompatible resourses for NET Core WindowsForms applications 
                // because the new deserializer of type "System.Resources.Extensions.DeserializingResourceReader" is used there.
                // Therefore unchanged original resourceBytes must be passed in EmbeddedResource constructor for an NET Core WindowsForms applications
                // if the readerType is the new type "System.Resources.Extensions.DeserializingResourceReader"
                return new EmbeddedResource(er.Name, er.Attributes, new MemoryStream(resourceBytes));
            }

            return new EmbeddedResource(er.Name, er.Attributes, output);
        }

        public static string[] GetRepackListFromResource(EmbeddedResource resource)
        {
            return GetRepackListFromStream(resource.GetResourceStream());
        }

        public static string[] GetRepackListFromStream(Stream stream)
        {
            return StringArrayBinaryFormatter.Deserialize(stream);
        }

        public static EmbeddedResource GenerateRepackListResource(IEnumerable<string> repackList)
        {
            var sorted = repackList.OrderBy(s => s).ToArray();
            var stream = new MemoryStream();
            StringArrayBinaryFormatter.Serialize(stream, sorted);
            return new EmbeddedResource(ILRepackListResourceName, ManifestResourceAttributes.Public, stream.ToArray());
        }

        private EmbeddedResource MergeEmbeddedResxResources(EmbeddedResource existing, EmbeddedResource incoming)
        {
            var entries = ReadResxEntries(existing);
            var incomingEntries = ReadResxEntries(incoming);

            // Fall back to last-wins for DeserializingResourceReader blobs (bugfix #277 format)
            if (entries == null || incomingEntries == null)
                return incoming;

            foreach (var kvp in incomingEntries)
                entries[kvp.Key] = kvp.Value;

            // Pass byte[] to EmbeddedResource so GetResourceStream() returns a fresh seekable
            // MemoryStream on each call (stream-based constructor hands ownership to the caller).
            byte[] merged;
            using (var output = new MemoryStream())
            {
                using (var rw = new ResourceWriter(output))
                {
                    foreach (var write in entries.Values)
                        write(rw);
                }  // ResourceWriter.Dispose calls Generate()
                merged = output.ToArray();
            }
            return new EmbeddedResource(existing.Name, existing.Attributes, merged);
        }

        private static Dictionary<string, Action<ResourceWriter>> ReadResxEntries(EmbeddedResource er)
        {
            var entries = new Dictionary<string, Action<ResourceWriter>>();
            using (var rr = new ResReader(er.GetResourceStream()))
            {
                // null when resMgrHeaderVersion > 1; also guard DeserializingResourceReader format
                if (rr.ReaderType?.StartsWith("System.Resources.Extensions.DeserializingResourceReader") == true)
                    return null;

                foreach (var res in rr)
                {
                    var name = res.name;
                    if (res.IsString)
                    {
                        var value = (string)rr.GetObject(res);
                        entries[name] = rw => rw.AddResource(name, value);
                    }
                    else if (res.typeCode == (int)ResourceTypeCode.ByteArray)
                    {
                        var bytes = (byte[])rr.GetObject(res);
                        entries[name] = rw => rw.AddResource(name, bytes);
                    }
                    else if (res.typeCode == (int)ResourceTypeCode.Stream)
                    {
                        // Capture bytes now; the MemoryStream returned by GetObject is tied to
                        // the ResReader's underlying stream which is disposed after this loop.
                        var bytes = ((MemoryStream)rr.GetObject(res)).ToArray();
                        entries[name] = rw => rw.AddResource(name, new MemoryStream(bytes));
                    }
                    else if (res.typeCode > (int)ResourceTypeCode.Null &&
                             res.typeCode < (int)ResourceTypeCode.StartOfUserTypes)
                    {
                        // Remaining built-in primitives: Boolean, Char, Int32, DateTime, etc.
                        var value = rr.GetObject(res);
                        entries[name] = rw => rw.AddResource(name, value);
                    }
                    else
                    {
                        // User-defined types: type is a real CLR assembly-qualified name.
                        var type = res.type;
                        var data = res.data;
                        entries[name] = rw => rw.AddResourceData(name, type, data);
                    }
                }
            }
            return entries;
        }
    }
}
