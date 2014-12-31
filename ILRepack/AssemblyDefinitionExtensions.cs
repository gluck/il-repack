using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ILRepacking
{
	public static class AssemblyDefinitionExtensions
	{
		public static string GetPortableProfileDirectory (this AssemblyDefinition assembly)
		{
			foreach (var custom in assembly.CustomAttributes) {
				if (custom.AttributeType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute") {
					var displayName = custom.Properties.FirstOrDefault(item => item.Name == "FrameworkDisplayName").Argument.Value;
					if (displayName == null) {
						return null;
					}

					var framework = displayName.ToString ();
					if (!string.Equals (framework, ".NET Portable Subset")) {
						return null;
					}

					var parts = custom.ConstructorArguments [0].Value.ToString ().Split (',');
					var root = Environment.ExpandEnvironmentVariables (
						Path.Combine (
							"%systemdrive%",
							"Program Files (x86)"));
					return Environment.ExpandEnvironmentVariables (
						Path.Combine (
							"%systemdrive%", Path.Combine (
							Directory.Exists (root) ? "Program Files (x86)" : "Program Files", 
							Path.Combine("Reference Assemblies", 
							Path.Combine("Microsoft", 
							Path.Combine("Framework",
							Path.Combine(parts [0],
							Path.Combine((parts [1].Split ('=')) [1],
							Path.Combine("Profile",
							(parts [2].Split ('=')) [1]))))))))); 
				}
			}

			return null;
		}
	}
}