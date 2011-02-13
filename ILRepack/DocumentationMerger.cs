using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Mono.Cecil;

namespace ILRepacking
{
    internal static class DocumentationMerger
    {
        internal static void Process(ILRepack repack)
        {
            try
            {
                var validXmlFiles = new List<XmlDocument>();
                XmlDocument doc;
                foreach (string assembly in repack.MergedAssemblyFiles)
                {
                    string assemblyDoc = Path.ChangeExtension(assembly, ".xml");
                    if (File.Exists (assemblyDoc))
                    {
                        doc = new XmlDocument();
                        doc.Load (assemblyDoc);
                        validXmlFiles.Add(doc);
                    }
                }
    			
                if (validXmlFiles.Count == 0)
                    return;

                doc = new XmlDocument();
                XmlElement root = doc.CreateElement("doc");
                doc.AppendChild(root);

                // assembly name
                var node = doc.CreateElement("assembly");
                root.AppendChild(node);
                var node2 = doc.CreateElement("name");
                node.AppendChild(node2);
                node2.AppendChild(doc.CreateTextNode(repack.TargetAssemblyDefinition.Name.Name));

                // members
                node = doc.CreateElement("members");
                root.AppendChild(node);
                foreach (var xml in validXmlFiles)
                {
                    XPathNodeIterator iterator = xml.CreateNavigator().Select("/doc/members/member");
                    while (iterator.MoveNext())
                    {
                        XPathNavigator navigator = iterator.Current;
                        node.AppendChild(doc.ImportNode((XmlNode) navigator.UnderlyingObject, true));
                    }
                }

                // write
                using (var writer = XmlWriter.Create(Path.ChangeExtension(repack.OutputFile, ".xml"), new XmlWriterSettings() { Indent = true, IndentChars = "    " }))
                {
                    doc.WriteTo(writer);
                }
            }
            catch (Exception e)
            {
                repack.ERROR("Failed to merge documentation files: " + e);
            }
        }
    }
}
