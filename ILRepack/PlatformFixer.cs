using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Mono.Cecil;

namespace ILRepacking
{
    class PlatformFixer
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
                if (!File.Exists(Path.Combine(targetPlatformDirectory, "mscorlib.dll")))
                    throw new ArgumentException("Invalid platform directory: \"" + targetPlatformDirectory + "\" (mscorlib.dll not found).");
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

        public TypeReference FixPlatformVersion(TypeReference reference)
        {
            AssemblyNameReference scopeAsm = reference.Scope as AssemblyNameReference;
            if (scopeAsm != null)
            {
                if (targetPlatformDirectory != null)
                {
                    string platformFile = Path.Combine(targetPlatformDirectory, scopeAsm.Name + ".dll");
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
                            {
                                TypeReference fmt = FixPlatformVersion(((OptionalModifierType)reference).ModifierType);
                                return new OptionalModifierType(fmt, fet);
                            }
                            else if (reference is RequiredModifierType)
                            {
                                TypeReference fmt = FixPlatformVersion(((RequiredModifierType)reference).ModifierType);
                                return new RequiredModifierType(fmt, fet);
                            }
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
                            throw new NotImplementedException();
                        newTypeRef.IsValueType = reference.IsValueType;
                        if (reference.DeclaringType != null)
                            newTypeRef.DeclaringType = FixPlatformVersion(reference.DeclaringType);
                        return newTypeRef;
                    }
                }
            }
            else
            {
                if (reference.Scope != null)
                    Console.WriteLine("PlatformFixer found unknown scope \"" + reference.Scope + "\".");
            }
            return reference;
        }

        public MethodReference FixPlatformVersion(MethodReference reference)
        {
            if (targetPlatformDirectory == null)
                return reference;
            MethodReference fixedRef = new MethodReference(reference.Name, FixPlatformVersion(reference.ReturnType), FixPlatformVersion(reference.DeclaringType));
            fixedRef.HasThis = reference.HasThis;
            fixedRef.ExplicitThis = reference.ExplicitThis;
            fixedRef.CallingConvention = reference.CallingConvention;
            foreach (ParameterDefinition pd in reference.Parameters)
            {
                fixedRef.Parameters.Add(FixPlatformVersion(pd));
            }
            foreach (GenericParameter gp in reference.GenericParameters)
            {
                reference.GenericParameters.Add(FixPlatformVersion(gp, fixedRef));
            }
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
            {
                npd.CustomAttributes.Add(FixPlatformVersion(ca));
            }
            npd.MarshalInfo = pd.MarshalInfo;
            return npd;
        }

        private GenericParameter FixPlatformVersion(GenericParameter gp, IGenericParameterProvider gpp)
        {
            GenericParameter ngp = new GenericParameter(gp.Name, gpp);
            ngp.Attributes = gp.Attributes;
            foreach (TypeReference tr in gp.Constraints)
            {
                ngp.Constraints.Add(FixPlatformVersion(tr));
            }
            foreach (CustomAttribute ca in gp.CustomAttributes)
            {
                ngp.CustomAttributes.Add(FixPlatformVersion(ca));
            }
            ngp.DeclaringType = FixPlatformVersion(gp.DeclaringType);
            foreach (GenericParameter gp1 in gp.GenericParameters)
            {
                ngp.GenericParameters.Add(FixPlatformVersion(gp1, ngp));
            }
            return ngp;
        }

        private CustomAttribute FixPlatformVersion(CustomAttribute ca)
        {
            CustomAttribute nca = new CustomAttribute(FixPlatformVersion(ca.Constructor));
            foreach (CustomAttributeArgument caa in ca.ConstructorArguments)
            {
                nca.ConstructorArguments.Add(FixPlatformVersion(caa));
            }
            foreach (CustomAttributeNamedArgument cana in ca.Fields)
            {
                nca.Fields.Add(FixPlatformVersion(cana));
            }
            foreach (CustomAttributeNamedArgument cana in ca.Properties)
            {
                nca.Properties.Add(FixPlatformVersion(cana));
            }
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
