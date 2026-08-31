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
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ILRepacking.Steps
{
    /// <summary>
    /// Makes preserialized (System.Resources.Extensions) resources work after that assembly has been
    /// merged in, so the output does not need a System.Resources.Extensions.dll next to it.
    ///
    /// Merging the types is not enough. A .resources stream written by PreserializedResourceWriter
    /// names its reader in the stream header, and DeserializingResourceReader re-reads that header and
    /// refuses to continue unless the name matches its own hardcoded identity. Both sides therefore
    /// have to be re-pointed at the merged assembly:
    ///
    ///   - the reader and resource-set type names in every .resources header
    ///   - the ldstr identity constants inside the merged System.Resources.Extensions code
    ///
    /// On .NET Framework there is one more problem: ManifestBasedResourceGroveler resolves the reader
    /// with Type.GetType, which binds in the load context and so cannot see an assembly that was
    /// LoadFrom'd from outside the application base - a plugin, typically. A module initializer that
    /// answers AssemblyResolve for the assembly's own name closes that gap.
    /// </summary>
    internal class SelfContainedResourcesStep : IRepackStep
    {
        private const string SreAssemblyName = "System.Resources.Extensions";
        private const string ReaderTypeName = "System.Resources.Extensions.DeserializingResourceReader";
        private const string ResourceSetTypeName = "System.Resources.Extensions.RuntimeResourceSet";
        private const int ResourceMagicNumber = unchecked((int)0xBEEFCACE);

        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly RepackOptions _options;

        public SelfContainedResourcesStep(ILogger logger, IRepackContext repackContext, RepackOptions options)
        {
            _logger = logger;
            _repackContext = repackContext;
            _options = options;
        }

        public void Perform()
        {
            if (!_options.SelfContainedResources)
                return;

            var sre = _repackContext.MergedAssemblies
                .FirstOrDefault(a => a.Name.Name == SreAssemblyName);
            if (sre == null)
            {
                _logger.Verbose($"Self-contained resources requested but {SreAssemblyName} was not merged - skip");
                return;
            }

            var targetQualifier = GetQualifier(_repackContext.TargetAssemblyDefinition.Name);
            _logger.Info($"Making preserialized resources self-contained, re-pointing them at '{targetQualifier}'");

            int constants = PatchIdentityConstants(targetQualifier);
            var mergedAssemblyNames = new HashSet<string>(
                _repackContext.MergedAssemblies
                    .Select(a => a.Name.Name)
                    .Where(n => !string.IsNullOrEmpty(n)),
                StringComparer.OrdinalIgnoreCase);
            var targetTypeNames = new HashSet<string>(
                _repackContext.MergedAssemblies
                    .SelectMany(a => a.MainModule.GetTypes())
                    .Concat(_repackContext.TargetAssemblyMainModule.GetTypes())
                    .Select(t => t.FullName),
                StringComparer.Ordinal);
            int headers = PatchResourceHeaders(targetQualifier, mergedAssemblyNames, targetTypeNames);
            _logger.Info($"- rewrote {constants} identity constant(s) and {headers} resource header(s)");

            if (headers > 0)
                InjectSelfResolvingModuleInitializer();
        }

        private static string GetQualifier(AssemblyNameReference name)
        {
            var token = name.PublicKeyToken;
            var tokenText = token != null && token.Length > 0
                ? string.Concat(token.Select(b => b.ToString("x2")))
                : "null";
            var culture = string.IsNullOrEmpty(name.Culture) ? "neutral" : name.Culture;
            return $"{name.Name}, Version={name.Version}, Culture={culture}, PublicKeyToken={tokenText}";
        }

        /// <summary>
        /// Rewrites the "&lt;type&gt;, System.Resources.Extensions, ..." literals the merged code compares
        /// against, so that it accepts and produces headers naming the merged assembly.
        /// </summary>
        private int PatchIdentityConstants(string targetQualifier)
        {
            int count = 0;
            foreach (var type in _repackContext.TargetAssemblyMainModule.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode != OpCodes.Ldstr)
                            continue;

                        var rewritten = RewriteQualifier(instruction.Operand as string, targetQualifier);
                        if (rewritten == null)
                            continue;

                        _logger.Verbose($"- {type.FullName}::{method.Name}: {rewritten}");
                        instruction.Operand = rewritten;
                        count++;
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Returns the assembly-qualified name with its assembly part swapped for <paramref name="targetQualifier"/>,
        /// or null when <paramref name="value"/> is not one of the two names we care about.
        /// </summary>
        private static string RewriteQualifier(string value, string targetQualifier)
        {
            if (value == null)
                return null;

            foreach (var typeName in new[] { ReaderTypeName, ResourceSetTypeName })
            {
                var prefix = typeName + ", " + SreAssemblyName;
                if (value == prefix || value.StartsWith(prefix + ",", StringComparison.Ordinal))
                    return typeName + ", " + targetQualifier;
            }
            return null;
        }

        private int PatchResourceHeaders(
            string targetQualifier,
            ISet<string> mergedAssemblyNames,
            ISet<string> targetTypeNames)
        {
            var resources = _repackContext.TargetAssemblyMainModule.Resources;
            int count = 0;

            for (int i = 0; i < resources.Count; i++)
            {
                if (!(resources[i] is EmbeddedResource embedded))
                    continue;
                if (!embedded.Name.EndsWith(".resources", StringComparison.Ordinal))
                    continue;

                var resourceData = embedded.GetResourceData();
                // GetResourceData consumes a stream-backed resource's stream, and Cecil's second read
                // at write time would produce all zeros - re-anchor the resource to the bytes just read.
                resources[i] = embedded = new EmbeddedResource(embedded.Name, embedded.Attributes, resourceData);

                byte[] patched;
                try
                {
                    patched = PatchResourceHeader(
                        resourceData, targetQualifier, mergedAssemblyNames, targetTypeNames);
                }
                catch (Exception exception)
                {
                    // a malformed .resources blob must not abort the merge
                    _logger.Warn($"Could not parse resource '{embedded.Name}', leaving it unpatched: {exception.Message}");
                    continue;
                }
                if (patched == null)
                    continue;

                _logger.Verbose($"- header of {embedded.Name}");
                resources[i] = new EmbeddedResource(embedded.Name, embedded.Attributes, patched);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Rewrites the reader/resource-set type names and resource type table. Their length can change,
        /// which moves everything after them, so the header size, the 8-byte alignment padding in front
        /// of the name hash table and the absolute data section offset all have to be recomputed.
        /// </summary>
        internal static byte[] PatchResourceHeader(
            byte[] bytes,
            string targetQualifier,
            ISet<string> mergedAssemblyNames,
            ISet<string> targetTypeNames)
        {
            using (var input = new MemoryStream(bytes))
            using (var reader = new BinaryReader(input, Encoding.UTF8))
            {
                if (bytes.Length < 4 || reader.ReadInt32() != ResourceMagicNumber)
                    return null;

                int headerVersion = reader.ReadInt32();
                reader.ReadInt32();                                     // bytes to skip, recomputed below
                string readerType = reader.ReadString();
                string resourceSetType = reader.ReadString();

                var newReaderType = RewriteQualifier(readerType, targetQualifier);
                var newResourceSetType = RewriteQualifier(resourceSetType, targetQualifier);
                if (newReaderType == null || newResourceSetType == null)
                    return null;

                int resourceSetVersion = reader.ReadInt32();
                int resourceCount = reader.ReadInt32();
                int typeCount = reader.ReadInt32();
                var typeNames = new string[typeCount];
                for (int i = 0; i < typeCount; i++)
                    typeNames[i] = RewriteMergedTypeQualifier(
                        reader.ReadString(), targetQualifier, mergedAssemblyNames, targetTypeNames);
                long afterTypes = input.Position;

                long afterAlignment = afterTypes + ((8 - (afterTypes & 7)) & 7);
                input.Position = afterAlignment + (resourceCount * 8L); // name hashes + name positions
                int dataSectionOffset = reader.ReadInt32();
                long tailStart = input.Position;                        // name and data sections are self-relative

                using (var output = new MemoryStream(bytes.Length))
                using (var writer = new BinaryWriter(output, Encoding.UTF8))
                {
                    writer.Write(ResourceMagicNumber);
                    writer.Write(headerVersion);
                    writer.Write(MeasurePrefixedStrings(newReaderType, newResourceSetType));
                    writer.Write(newReaderType);
                    writer.Write(newResourceSetType);
                    writer.Flush();

                    writer.Write(resourceSetVersion);
                    writer.Write(resourceCount);
                    writer.Write(typeCount);
                    foreach (var typeName in typeNames)
                        writer.Write(typeName);
                    writer.Flush();
                    int padding = (int)((8 - (output.Position & 7)) & 7);
                    for (int i = 0; i < padding; i++)
                        writer.Write((byte)"PAD"[i % 3]); // same filler ResourceWriter uses
                    writer.Flush();

                    int shift = (int)(output.Position - afterAlignment);
                    writer.Write(bytes, (int)afterAlignment, resourceCount * 8);
                    writer.Write(dataSectionOffset + shift);
                    writer.Write(bytes, (int)tailStart, bytes.Length - (int)tailStart);
                    writer.Flush();

                    return output.ToArray();
                }
            }
        }

        /// <summary>
        /// Resource type names are assembly-qualified. When their assembly was merged, the resource
        /// reader must resolve the type from the repacked assembly instead of loading the removed input.
        /// </summary>
        private static string RewriteMergedTypeQualifier(
            string value,
            string targetQualifier,
            ISet<string> mergedAssemblyNames,
            ISet<string> targetTypeNames)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var separatorIndex = FindAssemblySeparator(value);
            var typeName = separatorIndex < 0 ? value : value.Substring(0, separatorIndex);

            // A type the merge brought into the target wins even when the entry names an assembly that
            // was not merged: the entry may use an old identity (e.g. System.Drawing) that merely
            // type-forwards into an assembly that was merged (e.g. System.Drawing.Common).
            if (targetTypeNames != null && targetTypeNames.Contains(typeName))
                return typeName + ", " + targetQualifier;

            if (separatorIndex < 0)
                return value;

            var assemblySpec = value.Substring(separatorIndex + 1).TrimStart();
            var assemblyNameEnd = assemblySpec.IndexOf(',');
            var assemblyName = (assemblyNameEnd < 0 ? assemblySpec : assemblySpec.Substring(0, assemblyNameEnd)).TrimEnd();
            if (mergedAssemblyNames != null && mergedAssemblyNames.Contains(assemblyName))
                return typeName + ", " + targetQualifier;

            return value;
        }

        /// <summary>
        /// Returns the index of the comma separating the type name from its assembly qualifier,
        /// ignoring commas nested in generic argument brackets, or -1 for an unqualified name.
        /// </summary>
        private static int FindAssemblySeparator(string value)
        {
            int depth = 0;
            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '[')
                    depth++;
                else if (c == ']')
                    depth--;
                else if (c == ',' && depth == 0)
                    return i;
            }
            return -1;
        }

        private static int MeasurePrefixedStrings(params string[] values)
        {
            using (var probe = new MemoryStream())
            using (var writer = new BinaryWriter(probe, Encoding.UTF8))
            {
                foreach (var value in values)
                    writer.Write(value);
                writer.Flush();
                return (int)probe.Length;
            }
        }

        /// <summary>
        /// Emits, and calls from the module initializer, a handler that returns this assembly whenever
        /// something asks for it by name. Without it Type.GetType cannot find the merged reader on
        /// .NET Framework when the assembly was loaded from outside the application base.
        /// </summary>
        private void InjectSelfResolvingModuleInitializer()
        {
            var module = _repackContext.TargetAssemblyMainModule;
            var moduleType = module.Types.FirstOrDefault(t => t.Name == "<Module>");
            if (moduleType == null)
            {
                moduleType = new TypeDefinition(
                    string.Empty,
                    "<Module>",
                    TypeAttributes.NotPublic | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                    module.TypeSystem.Object);
                module.Types.Add(moduleType);
            }

            const string handlerName = "ILRepack_ResolveSelf";
            if (moduleType.Methods.Any(m => m.Name == handlerName))
                return;

            var corlib = module.TypeSystem.CoreLibrary;
            var assemblyType = new TypeReference("System.Reflection", "Assembly", module, corlib);
            var assemblyNameType = new TypeReference("System.Reflection", "AssemblyName", module, corlib);
            var resolveArgsType = new TypeReference("System", "ResolveEventArgs", module, corlib);
            var resolveHandlerType = new TypeReference("System", "ResolveEventHandler", module, corlib);
            var appDomainType = new TypeReference("System", "AppDomain", module, corlib);

            var getExecutingAssembly = new MethodReference("GetExecutingAssembly", assemblyType, assemblyType) { HasThis = false };
            var getName = new MethodReference("GetName", assemblyNameType, assemblyType) { HasThis = true };
            var getAssemblyNameName = new MethodReference("get_Name", module.TypeSystem.String, assemblyNameType) { HasThis = true };
            var getArgsName = new MethodReference("get_Name", module.TypeSystem.String, resolveArgsType) { HasThis = true };
            var assemblyNameCtor = new MethodReference(".ctor", module.TypeSystem.Void, assemblyNameType) { HasThis = true };
            assemblyNameCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            var stringEquals = new MethodReference("op_Equality", module.TypeSystem.Boolean, module.TypeSystem.String) { HasThis = false };
            stringEquals.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            stringEquals.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

            var handler = new MethodDefinition(
                handlerName,
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
                assemblyType);
            handler.Parameters.Add(new ParameterDefinition("sender", ParameterAttributes.None, module.TypeSystem.Object));
            handler.Parameters.Add(new ParameterDefinition("args", ParameterAttributes.None, resolveArgsType));

            var il = handler.Body.GetILProcessor();
            var notOurs = il.Create(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, getArgsName);
            il.Emit(OpCodes.Newobj, assemblyNameCtor);
            il.Emit(OpCodes.Callvirt, getAssemblyNameName);
            il.Emit(OpCodes.Call, getExecutingAssembly);
            il.Emit(OpCodes.Callvirt, getName);
            il.Emit(OpCodes.Callvirt, getAssemblyNameName);
            il.Emit(OpCodes.Call, stringEquals);
            il.Emit(OpCodes.Brfalse_S, notOurs);
            il.Emit(OpCodes.Call, getExecutingAssembly);
            il.Emit(OpCodes.Ret);
            il.Append(notOurs);
            il.Emit(OpCodes.Ret);
            moduleType.Methods.Add(handler);

            var addAssemblyResolve = new MethodReference("add_AssemblyResolve", module.TypeSystem.Void, appDomainType) { HasThis = true };
            addAssemblyResolve.Parameters.Add(new ParameterDefinition(resolveHandlerType));
            var getCurrentDomain = new MethodReference("get_CurrentDomain", appDomainType, appDomainType) { HasThis = false };
            var handlerCtor = new MethodReference(".ctor", module.TypeSystem.Void, resolveHandlerType) { HasThis = true };
            handlerCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            handlerCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));

            var initializer = moduleType.Methods.FirstOrDefault(m => m.Name == ".cctor" && m.IsStatic);
            if (initializer == null)
            {
                initializer = new MethodDefinition(
                    ".cctor",
                    MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig |
                    MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                    module.TypeSystem.Void);
                initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
                moduleType.Methods.Add(initializer);
            }

            var body = initializer.Body.GetILProcessor();
            var first = initializer.Body.Instructions[0];
            body.InsertBefore(first, body.Create(OpCodes.Call, getCurrentDomain));
            body.InsertBefore(first, body.Create(OpCodes.Ldnull));
            body.InsertBefore(first, body.Create(OpCodes.Ldftn, handler));
            body.InsertBefore(first, body.Create(OpCodes.Newobj, handlerCtor));
            body.InsertBefore(first, body.Create(OpCodes.Callvirt, addAssemblyResolve));

            _logger.Info("- injected a module initializer that resolves the merged assembly by name");
        }
    }
}
