using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.XPath;

using Mono.Cecil;
using Mono.Linker.Steps;

namespace ILRepacking.Steps.Linker
{

	public class BlacklistStep : BaseStep {

		protected override void Process ()
		{
			foreach (string name in Assembly.GetExecutingAssembly ().GetManifestResourceNames ()) {
				if (!name.EndsWith (".xml", StringComparison.OrdinalIgnoreCase) || !IsReferenced (GetAssemblyName (name)))
					continue;

				try {
					if (Context.LogInternalExceptions)
						Console.WriteLine ("Processing resource linker descriptor: {0}", name);
					Context.Pipeline.AddStepAfter (typeof (TypeMapStep), GetResolveStep (name));
				} catch (XmlException ex) {
					/* This could happen if some broken XML file is included. */
					if (Context.LogInternalExceptions)
						Console.WriteLine ("Error processing {0}: {1}", name, ex);
				}
			}

			foreach (var asm in Context.GetAssemblies ()) {
				foreach (var rsc in asm.Modules
									.SelectMany (mod => mod.Resources)
									.Where (res => res.ResourceType == ResourceType.Embedded)
									.Where (res => res.Name.EndsWith (".xml", StringComparison.OrdinalIgnoreCase))
									.Where (res => IsReferenced (GetAssemblyName (res.Name)))
									.Cast<EmbeddedResource> ()) {
					try {
						if (Context.LogInternalExceptions)
							Console.WriteLine ("Processing embedded resource linker descriptor: {0}", rsc.Name);

						Context.Pipeline.AddStepAfter (typeof (TypeMapStep), GetExternalResolveStep (rsc, asm));
					} catch (XmlException ex) {
						/* This could happen if some broken XML file is embedded. */
						if (Context.LogInternalExceptions)
							Console.WriteLine ("Error processing {0}: {1}", rsc.Name, ex);
					}
				}
			}
		}

		static string GetAssemblyName (string descriptor)
		{
			int pos = descriptor.LastIndexOf ('.');
			if (pos == -1)
				return descriptor;

			return descriptor.Substring (0, pos);
		}

		bool IsReferenced (string name)
		{
			foreach (AssemblyDefinition assembly in Context.GetAssemblies ())
				if (assembly.Name.Name == name)
					return true;

			return false;
		}

		static ResolveFromXmlStep GetExternalResolveStep (EmbeddedResource resource, AssemblyDefinition assembly)
		{
			return new ResolveFromXmlStep (GetExternalDescriptor (resource), "resource " + resource.Name + " in " + assembly.FullName);
		}

		static ResolveFromXmlStep GetResolveStep (string descriptor)
		{
			return new ResolveFromXmlStep (GetDescriptor (descriptor), "descriptor " + descriptor + " from " + Assembly.GetExecutingAssembly ().FullName);
		}

		static XPathDocument GetExternalDescriptor (EmbeddedResource resource)
		{
			using (var sr = new StreamReader (resource.GetResourceStream ())) {
				return new XPathDocument (new StringReader (sr.ReadToEnd ()));
			}
		}

		static XPathDocument GetDescriptor (string descriptor)
		{
			using (StreamReader sr = new StreamReader (GetResource (descriptor))) {
				return new XPathDocument (new StringReader (sr.ReadToEnd ()));
			}
		}

		static Stream GetResource (string descriptor)
		{
			return Assembly.GetExecutingAssembly ().GetManifestResourceStream (descriptor);
		}
	}
}
