using Mono.Cecil;
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
using System.IO;
using System.Resources;

namespace ILRepacking.Steps.ResourceProcessing
{
    internal class GenericResourceProcessor : IResProcessor
    {
        private readonly IRepackContext _repackContext;

        public GenericResourceProcessor(IRepackContext repackContext)
        {
            _repackContext = repackContext;
        }

        public bool Process(Res resource, AssemblyDefinition containingAssembly, EmbeddedResource embeddedResource, ResReader resourceReader, ResourceWriter resourceWriter)
        {
            string fix = _repackContext.FixStr(resource.type);
            if (fix == resource.type)
            {
                resourceWriter.AddResourceData(resource.name, resource.type, resource.data);
            }
            else
            {
                var output2 = new MemoryStream(resource.data.Length);
                var sr = new SerReader(_repackContext, new MemoryStream(resource.data), output2);
                sr.Stream();
                resourceWriter.AddResourceData(resource.name, fix, output2.ToArray());
            }

            return true;
        }
    }
}
