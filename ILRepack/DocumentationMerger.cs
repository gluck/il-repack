//
// Copyright (c) 2011 Francois Valdy
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
                    if (File.Exists(assemblyDoc))
                    {
                        doc = new XmlDocument();
                        doc.Load(assemblyDoc);
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
                        node.AppendChild(doc.ImportNode((XmlNode)navigator.UnderlyingObject, true));
                    }
                }

                // write
                using (var writer = XmlWriter.Create(Path.ChangeExtension(repack.Options.OutputFile, ".xml"), new XmlWriterSettings() { Indent = true, IndentChars = "    " }))
                {
                    doc.WriteTo(writer);
                }
            }
            catch (Exception e)
            {
                repack.Logger.Error("Failed to merge documentation files: " + e);
            }
        }
    }
}
