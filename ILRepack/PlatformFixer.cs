//
// Copyright (c) 2011 Simon Goldschmidt
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
//
using ILRepacking.Mixins;
using Mono.Cecil;
using System;
using System.Collections;
using System.IO;
using System.Linq;

namespace ILRepacking
{
    internal class PlatformAndDuplicateFixer : PlatformFixer
    {
        private readonly IRepackContext _repack;

        public PlatformAndDuplicateFixer(IRepackContext repack, TargetRuntime runtime) : base(repack, runtime)
        {
            _repack = repack;
        }

        protected override IMetadataScope GetFixedPlatformVersion(AssemblyNameReference assyName)
        {
            var baseResult = base.GetFixedPlatformVersion(assyName);
            if (baseResult is AssemblyNameReference refName)
            {
                assyName = refName;
            }
            var sameRef = _repack.TargetAssemblyMainModule.AssemblyReferences.FirstOrDefault(a => a.Name == assyName.Name);
            if (sameRef != null && sameRef.Version > assyName.Version)
            {
                var ret = _repack.MergeScope(sameRef);
                return ret;
            }

            return baseResult;
        }
    }

    internal class PlatformFixer
    {
        readonly IRepackContext repack;
        private TargetRuntime sourceRuntime;
        private TargetRuntime targetRuntime;
        private string targetPlatformDirectory;
        /// <summary>Loaded assemblies are stored here to prevent them loading more than once.</summary>
        private Hashtable platformAssemblies = new Hashtable();

        public PlatformFixer(IRepackContext repack, TargetRuntime runtime)
        {
            this.repack = repack;
            sourceRuntime = runtime;
        }

        public void ParseTargetPlatformDirectory(TargetRuntime runtime, string platformDirectory)
        {
            targetRuntime = runtime;
            targetPlatformDirectory = platformDirectory;

            if (!string.IsNullOrEmpty(targetPlatformDirectory))
            {
                if (!Directory.Exists(targetPlatformDirectory))
                    throw new ArgumentException("Platform directory not found: \"" + targetPlatformDirectory + "\".");
            }
        }

        private AssemblyDefinition TryGetPlatformAssembly(AssemblyNameReference sourceAssemblyName)
        {
            try
            {
                string platformFile = Path.Combine(targetPlatformDirectory, sourceAssemblyName.Name + ".dll");
                AssemblyDefinition platformAsm = null;
                platformAsm = (AssemblyDefinition)platformAssemblies[platformFile];
                if (platformAsm == null)
                {
                    if (File.Exists(platformFile))
                    {
                        // file exists, must be a platform file so exchange it // TODO: is this OK?
                        platformAsm = AssemblyDefinition.ReadAssembly(platformFile);
                        platformAssemblies[platformFile] = platformAsm;
                    }
                }
                return platformAsm;
            }
            catch
            {
                return null;
            }
        }

        public IMetadataScope FixPlatformVersion(AssemblyNameReference assyName)
        {
            return GetFixedPlatformVersion(assyName) ?? repack.MergeScope(assyName);
        }

        protected virtual IMetadataScope GetFixedPlatformVersion(AssemblyNameReference assyName)
        {
            if (targetPlatformDirectory == null)
                return null;

            AssemblyDefinition fixedDef = TryGetPlatformAssembly(assyName);
            if (fixedDef == null)
                return null;
            var ret = repack.MergeScope(fixedDef.Name);
            return ret;
        }

        public void FixPlatformVersion(ExportedType exported)
        {
            if (exported == null)
                return;

            AssemblyNameReference scopeAsm = exported.Scope as AssemblyNameReference;
            if (scopeAsm == null)
                return;

            var platformAsm = GetFixedPlatformVersion(scopeAsm);
            if (platformAsm == null)
                return;

            exported.Scope = platformAsm; 
        }

        public void FixPlatformVersion(GenericParameterConstraint constraint)
        {
            if (constraint == null)
                return;

            FixPlatformVersion(constraint.ConstraintType);
            if (constraint.HasCustomAttributes)
                foreach (CustomAttribute ca in constraint.CustomAttributes)
                    FixPlatformVersion(ca);
        }

        public void FixPlatformVersion(TypeReference reference)
        {
            if (reference == null)
                return;

            AssemblyNameReference scopeAsm = reference.Scope as AssemblyNameReference;
            if (scopeAsm == null)
                return;

            if (reference is TypeSpecification)
            {
                FixPlatformVersion(((TypeSpecification)reference).ElementType);
                if (reference is OptionalModifierType)
                    FixPlatformVersion(((OptionalModifierType)reference).ModifierType);
                else if (reference is RequiredModifierType)
                    FixPlatformVersion(((RequiredModifierType)reference).ModifierType);
                else if (reference is GenericInstanceType)
                {
                    var instance = (GenericInstanceType)reference;
                    FixPlatformVersion(instance.ElementType);
                    if (instance.HasGenericArguments)
                        foreach (var ga in instance.GenericArguments)
                            FixPlatformVersion(ga);
                }
                else if (reference is FunctionPointerType)
                {
                    var instance = (FunctionPointerType)reference;
                    FixPlatformVersion(instance.ReturnType);
                    if (instance.HasParameters)
                        foreach (var p in instance.Parameters)
                            FixPlatformVersion(p);
                }
            }
            else if (!(reference is GenericParameter))
            {
                var platformAsm = GetFixedPlatformVersion(scopeAsm);
                if (platformAsm == null) return;

                reference.Scope = platformAsm;
            }
            if (reference.HasGenericParameters)
                foreach (var gp in reference.GenericParameters)
                    FixPlatformVersion(gp);
            FixPlatformVersion(reference.DeclaringType);
        }


        void FixPlatformVersionOnMethodSpecification(MethodReference method)
        {
            if (!method.IsGenericInstance)
                throw new NotSupportedException();

            var instance = (GenericInstanceMethod)method;
            FixPlatformVersion(instance.ElementMethod);

            if (instance.HasGenericArguments)
                foreach (var ga in instance.GenericArguments)
                    FixPlatformVersion(ga);
        }


        public void FixPlatformVersion(MethodReference reference)
        {
            if (reference == null)
                return;

            if (reference.IsGenericInstance)
            {
                FixPlatformVersionOnMethodSpecification(reference);
                return;
            }

            FixPlatformVersion(reference.ReturnType);
            FixPlatformVersion(reference.DeclaringType);
            if (reference.HasParameters)
                foreach (ParameterDefinition pd in reference.Parameters)
                    FixPlatformVersion(pd);
            if (reference.HasGenericParameters)
                foreach (GenericParameter gp in reference.GenericParameters)
                    FixPlatformVersion(gp);
        }

        public void FixPlatformVersion(FieldReference reference)
        {
            if (reference == null)
                return;

            FixPlatformVersion(reference.FieldType);
            FixPlatformVersion(reference.DeclaringType);
        }

        private void FixPlatformVersion(ParameterDefinition pd)
        {
            FixPlatformVersion(pd.ParameterType);
            if (pd.HasCustomAttributes)
                foreach (CustomAttribute ca in pd.CustomAttributes)
                    FixPlatformVersion(ca);
        }

        private void FixPlatformVersion(GenericParameter gp)
        {
            if (gp.HasConstraints)
                foreach (var tr in gp.Constraints)
                    FixPlatformVersion(tr);
            if (gp.HasCustomAttributes)
                foreach (CustomAttribute ca in gp.CustomAttributes)
                    FixPlatformVersion(ca);
            if (gp.HasGenericParameters)
                foreach (GenericParameter gp1 in gp.GenericParameters)
                    FixPlatformVersion(gp1);
        }

        private void FixPlatformVersion(CustomAttribute ca)
        {
            FixPlatformVersion(ca.Constructor);
            if (ca.HasConstructorArguments)
                foreach (CustomAttributeArgument caa in ca.ConstructorArguments)
                    FixPlatformVersion(caa);
            if (ca.HasFields)
                foreach (CustomAttributeNamedArgument cana in ca.Fields)
                    FixPlatformVersion(cana);
            if (ca.HasProperties)
                foreach (CustomAttributeNamedArgument cana in ca.Properties)
                    FixPlatformVersion(cana);
        }

        private void FixPlatformVersion(CustomAttributeArgument caa)
        {
            FixPlatformVersion(caa.Type);
        }

        private void FixPlatformVersion(CustomAttributeNamedArgument cana)
        {
            FixPlatformVersion(cana.Argument);
        }
    }
}
