//
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
using System.Resources;

namespace ILRepacking.Steps.ResourceProcessing
{
    /// <summary>
    /// This collector removes the BAML streams from non-main assemblies and
    /// moves them in a assembly-name directory, to prevent duplication.
    /// </summary>
    internal class BamlStreamCollector : IResProcessor, IEmbeddedResourceProcessor
    {
        private readonly AssemblyDefinition _primaryAssemblyDefinition;

        public Dictionary<Res, AssemblyDefinition> BamlStreams { get; private set; }

        public BamlStreamCollector(AssemblyDefinition primaryAssemblyDefinition)
        {
            BamlStreams = new Dictionary<Res, AssemblyDefinition>();
            _primaryAssemblyDefinition = primaryAssemblyDefinition;
        }

        public bool Process(AssemblyDefinition containingAssembly, Res resource, ResReader resourceReader, ResourceWriter resourceWriter)
        {
            if (resource.IsBamlStream)
            {
                BamlStreams.Add(resource, containingAssembly);
                return true;
            }
            return false;
        }

        public void Process(EmbeddedResource embeddedResource, ResourceWriter resourceWriter)
        {
            foreach (var bamlStream in BamlStreams)
            {
                resourceWriter.AddResourceData(
                    GetResourceName(bamlStream.Key, bamlStream.Value), bamlStream.Key.type, bamlStream.Key.data);
            }
        }

        private string GetResourceName(Res resource, AssemblyDefinition assembly)
        {
            if (assembly == _primaryAssemblyDefinition)
                return resource.name;

            return string.Format("{0}/{1}", assembly.Name.Name.ToLowerInvariant(), resource.name);
        }
    }
}
