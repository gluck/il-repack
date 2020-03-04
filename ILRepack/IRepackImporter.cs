//
// Copyright (c) 2011 Francois Valdy
// Copyright (c) 2015 Timotei Dolean
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using Mono.Cecil;
using Mono.Collections.Generic;

namespace ILRepacking
{
    internal interface IRepackImporter
    {
        TypeReference Import(TypeReference reference, IGenericParameterProvider context);

        FieldReference Import(FieldReference reference, IGenericParameterProvider context);

        MethodReference Import(MethodReference reference);

        MethodReference Import(MethodReference reference, IGenericParameterProvider context);

        TypeDefinition Import(TypeDefinition type, Collection<TypeDefinition> col, bool internalize, bool rename);

        void Import(ExportedType type, Collection<ExportedType> col, ModuleDefinition module);
    }
}
