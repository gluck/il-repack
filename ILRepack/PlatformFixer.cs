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
using Mono.Cecil;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ILRepacking
{
    public class PlatformFixer
    {
        private TargetRuntime sourceRuntime;
        private TargetRuntime targetRuntime;
        private string targetPlatformDirectory;
        /// <summary>Loaded assemblies are stored here to prevent them loading more than once.</summary>
        private Hashtable platformAssemblies = new Hashtable();

        public PlatformFixer(TargetRuntime runtime)
        {
            sourceRuntime = runtime;
        }

        public void ParseTargetPlatformDirectory(TargetRuntime runtime, string platformDirectory)
        {
            targetRuntime = runtime;
            targetPlatformDirectory = platformDirectory;

            if (string.IsNullOrEmpty(targetPlatformDirectory) && (runtime != sourceRuntime))
                GetPlatformPath(runtime);
            if (!string.IsNullOrEmpty(targetPlatformDirectory))
            {
                if (!Directory.Exists(targetPlatformDirectory))
                    throw new ArgumentException("Platform directory not found: \"" + targetPlatformDirectory + "\".");
                // TODO: only tested for Windows, not for Mono!
//if (!File.Exists(Path.Combine(targetPlatformDirectory, "mscorlib.dll")))
  //                  throw new ArgumentException("Invalid platform directory: \"" + targetPlatformDirectory + "\" (mscorlib.dll not found).");
            }
        }

        protected void GetPlatformPath(TargetRuntime runtime)
        {
            // TODO: obviously, this only works for Windows, not for Mono!
            string platformBasePath = Path.GetFullPath(Path.Combine(Environment.SystemDirectory, "..\\Microsoft.NET\\Framework\\"));
            List<string> platformDirectories = new List<string>(Directory.GetDirectories(platformBasePath));
            switch (runtime)
            {
                case (TargetRuntime.Net_1_0):
                    targetPlatformDirectory = platformDirectories.First(x => Path.GetFileName(x).StartsWith("v1.0."));
                    break;
                case (TargetRuntime.Net_1_1):
                    targetPlatformDirectory = platformDirectories.First(x => Path.GetFileName(x).StartsWith("v1.1."));
                    break;
                case (TargetRuntime.Net_2_0):
                    targetPlatformDirectory = platformDirectories.First(x => Path.GetFileName(x).StartsWith("v2.0."));
                    break;
                case (TargetRuntime.Net_4_0):
                    targetPlatformDirectory = platformDirectories.First(x => Path.GetFileName(x).StartsWith("v4.0."));
                    break;
                default:
                    throw new NotImplementedException();
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

        public AssemblyNameReference FixPlatformVersion(AssemblyNameReference assyName)
        {
            if (targetPlatformDirectory == null)
                return assyName;

            AssemblyDefinition fixedDef = TryGetPlatformAssembly(assyName);
            if (fixedDef != null)
                return fixedDef.Name;

            return assyName;
        }

        public TypeReference FixPlatformVersion(TypeReference reference)
        {
            if (targetPlatformDirectory == null)
                return reference;

            AssemblyNameReference scopeAsm = reference.Scope as AssemblyNameReference;
            if (scopeAsm != null)
            {
                AssemblyDefinition platformAsm = TryGetPlatformAssembly(scopeAsm);
                if (platformAsm != null)
                {
                    TypeReference newTypeRef;
                    if (reference is TypeSpecification)
                    {
                        TypeSpecification refSpec = reference as TypeSpecification;
                        TypeReference fet = FixPlatformVersion(refSpec.ElementType);
                        if (reference is ArrayType)
                        {
                            var array = (ArrayType)reference;
                            var imported_array = new ArrayType(fet);
                            if (array.IsVector)
                                return imported_array;

                            var dimensions = array.Dimensions;
                            var imported_dimensions = imported_array.Dimensions;

                            imported_dimensions.Clear();

                            for (int i = 0; i < dimensions.Count; i++)
                            {
                                var dimension = dimensions[i];

                                imported_dimensions.Add(new ArrayDimension(dimension.LowerBound, dimension.UpperBound));
                            }

                            return imported_array;
                        }
                        else if (reference is PointerType)
                            return new PointerType(fet);
                        else if (reference is ByReferenceType)
                            return new ByReferenceType(fet);
                        else if (reference is PinnedType)
                            return new PinnedType(fet);
                        else if (reference is SentinelType)
                            return new SentinelType(fet);
                        else if (reference is OptionalModifierType)
                            return new OptionalModifierType(FixPlatformVersion(((OptionalModifierType)reference).ModifierType), fet);
                        else if (reference is RequiredModifierType)
                            return new RequiredModifierType(FixPlatformVersion(((RequiredModifierType)reference).ModifierType), fet);
                        else if (reference is GenericInstanceType)
                        {
                            var instance = (GenericInstanceType)reference;
                            var element_type = FixPlatformVersion(instance.ElementType);
                            var imported_instance = new GenericInstanceType(element_type);

                            var arguments = instance.GenericArguments;
                            var imported_arguments = imported_instance.GenericArguments;

                            for (int i = 0; i < arguments.Count; i++)
                                imported_arguments.Add(FixPlatformVersion(arguments[i]));

                            return imported_instance;
                        }
                        else if (reference is FunctionPointerType)
                            throw new NotImplementedException();
                        else
                            throw new InvalidOperationException();
                    }
                    else
                    {
                        newTypeRef = new TypeReference(reference.Namespace, reference.Name, reference.Module,
                            platformAsm.Name);
                    }
                    foreach (var gp in reference.GenericParameters)
                        newTypeRef.GenericParameters.Add(FixPlatformVersion(gp, newTypeRef));
                    newTypeRef.IsValueType = reference.IsValueType;
                    if (reference.DeclaringType != null)
                        newTypeRef.DeclaringType = FixPlatformVersion(reference.DeclaringType);
                    return newTypeRef;
                }
            }
            return reference;
        }


        MethodSpecification FixPlatformVersionOnMethodSpecification(MethodReference method)
        {
            if (!method.IsGenericInstance)
                throw new NotSupportedException();

            var instance = (GenericInstanceMethod)method;
            var element_method = FixPlatformVersion(instance.ElementMethod);
            var imported_instance = new GenericInstanceMethod(element_method);

            var arguments = instance.GenericArguments;
            var imported_arguments = imported_instance.GenericArguments;

            for (int i = 0; i < arguments.Count; i++)
                imported_arguments.Add(FixPlatformVersion(arguments[i]));

            return imported_instance;
        }


        public MethodReference FixPlatformVersion(MethodReference reference)
        {
            if (targetPlatformDirectory == null)
                return reference;

            if (reference.IsGenericInstance)
            {
                return FixPlatformVersionOnMethodSpecification(reference);
            }

            MethodReference fixedRef = new MethodReference(reference.Name, FixPlatformVersion(reference.ReturnType), FixPlatformVersion(reference.DeclaringType));
            fixedRef.HasThis = reference.HasThis;
            fixedRef.ExplicitThis = reference.ExplicitThis;
            fixedRef.CallingConvention = reference.CallingConvention;
            foreach (ParameterDefinition pd in reference.Parameters)
                fixedRef.Parameters.Add(FixPlatformVersion(pd));
            foreach (GenericParameter gp in reference.GenericParameters)
                fixedRef.GenericParameters.Add(FixPlatformVersion(gp, fixedRef));
            return fixedRef;
        }

        public FieldReference FixPlatformVersion(FieldReference reference)
        {
            if (targetPlatformDirectory == null)
                return reference;

            FieldReference fixedRef = new FieldReference(reference.Name, FixPlatformVersion(reference.FieldType), FixPlatformVersion(reference.DeclaringType));
            return fixedRef;
        }

        private ParameterDefinition FixPlatformVersion(ParameterDefinition pd)
        {
            ParameterDefinition npd = new ParameterDefinition(pd.Name, pd.Attributes, FixPlatformVersion(pd.ParameterType));
            npd.Constant = pd.Constant;
            foreach (CustomAttribute ca in pd.CustomAttributes)
                npd.CustomAttributes.Add(FixPlatformVersion(ca));
            npd.MarshalInfo = pd.MarshalInfo;
            return npd;
        }

        private GenericParameter FixPlatformVersion(GenericParameter gp, IGenericParameterProvider gpp)
        {
            GenericParameter ngp = new GenericParameter(gp.Name, gpp);
            ngp.Attributes = gp.Attributes;
            foreach (TypeReference tr in gp.Constraints)
                ngp.Constraints.Add(FixPlatformVersion(tr));
            foreach (CustomAttribute ca in gp.CustomAttributes)
                ngp.CustomAttributes.Add(FixPlatformVersion(ca));
            if (gp.DeclaringType != null )
                ngp.DeclaringType = FixPlatformVersion(gp.DeclaringType);
            foreach (GenericParameter gp1 in gp.GenericParameters)
                ngp.GenericParameters.Add(FixPlatformVersion(gp1, ngp));
            return ngp;
        }

        private CustomAttribute FixPlatformVersion(CustomAttribute ca)
        {
            CustomAttribute nca = new CustomAttribute(FixPlatformVersion(ca.Constructor));
            foreach (CustomAttributeArgument caa in ca.ConstructorArguments)
                nca.ConstructorArguments.Add(FixPlatformVersion(caa));
            foreach (CustomAttributeNamedArgument cana in ca.Fields)
                nca.Fields.Add(FixPlatformVersion(cana));
            foreach (CustomAttributeNamedArgument cana in ca.Properties)
                nca.Properties.Add(FixPlatformVersion(cana));
            return nca;
        }

        private CustomAttributeArgument FixPlatformVersion(CustomAttributeArgument caa)
        {
            return new CustomAttributeArgument(FixPlatformVersion(caa.Type), caa.Value);
        }

        private CustomAttributeNamedArgument FixPlatformVersion(CustomAttributeNamedArgument cana)
        {
            return new CustomAttributeNamedArgument(cana.Name, FixPlatformVersion(cana.Argument));
        }
    }
}
