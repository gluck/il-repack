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
using System.Resources;

namespace ILRepacking.Steps.ResourceProcessing
{
    internal class StringResourceProcessor : IResProcessor
    {
        private readonly IRepackContext _repackContext;

        public StringResourceProcessor(IRepackContext repackContext)
        {
            _repackContext = repackContext;
        }

        public bool Process(Res resource, AssemblyDefinition containingAssembly, EmbeddedResource embeddedResource, ResReader resourceReader, ResourceWriter resourceWriter)
        {
            if (!resource.IsString)
                return false;

            string content = (string)resourceReader.GetObject(resource);
            content = _repackContext.FixStr(content);
            resourceWriter.AddResource(resource.name, content);

            return true;
        }
    }
}
