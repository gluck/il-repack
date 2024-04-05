using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using ILRepacking.Steps;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace ILRepacking
{
    /// <summary>
    /// Merger of ILLink tool files. https://github.com/dotnet/runtime/blob/main/docs/workflow/trimming/ILLink-files.md
    /// Documentation of file formats: https://github.com/dotnet/runtime/blob/main/docs/tools/illink/data-formats.md
    /// </summary>
    internal class ILLinkFileMergeStep : IRepackStep
    {
        private const string DESCRIPTORS_FILE_NAME = "ILLink.Descriptors.xml";
        private const string SUBSTITUTIONS_FILE_NAME = "ILLink.Substitutions.xml";
        private const string SUPPRESSIONS_FILE_NAME = "ILLink.Suppressions.xml";
        private const string LINK_ATTRIBUTES_FILE_NAME = "ILLink.LinkAttributes.xml";

        private readonly ILRepack _repack;
        private readonly bool _mergeIlLinkerFiles;
        private readonly ILogger _logger;

        public ILLinkFileMergeStep(ILogger logger, ILRepack repack, RepackOptions options)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (repack == null) throw new ArgumentNullException(nameof(repack));
            if (options == null) throw new ArgumentNullException(nameof(options));

            _logger = logger;
            _repack = repack;
            _mergeIlLinkerFiles = options.MergeIlLinkerFiles;
        }

        /// <inheritdoc />
        public void Perform()
        {
            if (!_mergeIlLinkerFiles)
            {
                return;
            }

            try
            {
                var ilLinkSubstitutionsList = new List<XDocument>();
                var ilLinkDescriptorsList = new List<XDocument>();
                var ilLinkSuppressionsList = new List<XDocument>();
                var ilLinkLinkAttributesList = new List<XDocument>();

                foreach (var assemblyDef in _repack.MergedAssemblies)
                    foreach (var moduleDef in assemblyDef.Modules)
                        foreach (var resourceDef in moduleDef.Resources.OfType<EmbeddedResource>().ToList())
                        {
                            if (resourceDef.ResourceType != ResourceType.Embedded)
                            {
                                continue;
                            }

                            switch (resourceDef.Name)
                            {
                                case DESCRIPTORS_FILE_NAME:
                                    AddLinkerDocument(ilLinkDescriptorsList, resourceDef);
                                    break;
                                case SUBSTITUTIONS_FILE_NAME:
                                    AddLinkerDocument(ilLinkSubstitutionsList, resourceDef);
                                    break;
                                case SUPPRESSIONS_FILE_NAME:
                                    AddLinkerDocument(ilLinkSuppressionsList, resourceDef);
                                    break;
                                case LINK_ATTRIBUTES_FILE_NAME:
                                    AddLinkerDocument(ilLinkLinkAttributesList, resourceDef);
                                    break;
                                default:
                                    continue;
                            }

                            moduleDef.Resources.Remove(resourceDef); // prevent 'duplicate resource' warning 
                        }

                if (ilLinkDescriptorsList.Count > 0)
                {
                    _logger.Verbose($"Merging {DESCRIPTORS_FILE_NAME} files.");

                    var resource = MergeDescriptors(_repack, ilLinkDescriptorsList);
                    AddOrReplaceResource(_repack.TargetAssemblyMainModule.Resources, DESCRIPTORS_FILE_NAME, resource);
                }

                if (ilLinkSubstitutionsList.Count > 0)
                {
                    _logger.Verbose($"Merging {SUBSTITUTIONS_FILE_NAME} files.");

                    var resource = MergeSubstitutions(_repack, ilLinkSubstitutionsList);
                    AddOrReplaceResource(_repack.TargetAssemblyMainModule.Resources, SUBSTITUTIONS_FILE_NAME, resource);
                }

                if (ilLinkSuppressionsList.Count > 0)
                {
                    _logger.Verbose($"Merging {SUPPRESSIONS_FILE_NAME} files.");

                    var resource = MergeSuppressions(_repack, ilLinkSuppressionsList);
                    AddOrReplaceResource(_repack.TargetAssemblyMainModule.Resources, SUPPRESSIONS_FILE_NAME, resource);
                }

                if (ilLinkLinkAttributesList.Count > 0)
                {
                    _logger.Verbose($"Merging {LINK_ATTRIBUTES_FILE_NAME} files.");

                    var resource = MergeLinkAttributes(_repack, ilLinkLinkAttributesList);
                    AddOrReplaceResource(_repack.TargetAssemblyMainModule.Resources, LINK_ATTRIBUTES_FILE_NAME, resource);
                }
            }
            catch (Exception e)
            {
                _logger.Error("Failed to merge ILLink files: " + e);
            }
        }

        private static Resource MergeLinkAttributes(ILRepack repack, List<XDocument> list)
        {
            var repackTargetAssemblyName = repack.TargetAssemblyDefinition.Name.Name;
            var mergedAssemblyNames = new HashSet<string>(repack.MergedAssemblies.Select(asmDef => asmDef.Name.Name), StringComparer.Ordinal);

            var resourceStream = MergeIlLinkerDocuments(repack, list, UpdateAssembly);

            return new EmbeddedResource(LINK_ATTRIBUTES_FILE_NAME, ManifestResourceAttributes.Public, resourceStream);


            void UpdateAssembly(XElement assemblyElement)
            {
                // TODO for masks like fullname="AssemblyName.*"
                // When previous original assembly name matching mask and repacked *new* one is not
                // then duplicate <assembly> and leaving original <assembly> with mask as is
                // and modifying copy to have 'fullname=repackTargetAssemblyName'
                
                // substitute <assembly fullname=""> with repack target name
                assemblyElement.AddFirst(new XComment($" original {assemblyElement.Attribute("fullname")}"));
                assemblyElement.SetAttributeValue("fullname", repackTargetAssemblyName);

                foreach (var attributeElement in assemblyElement.Elements("attribute"))
                {
                    UpdateAttribute(attributeElement);
                }

                foreach (var typeElement in assemblyElement.Elements("type"))
                {
                    UpdateType(typeElement);
                }
            }


            void UpdateAttribute(XElement attributeElement)
            {
                var assemblyName = attributeElement.Attribute("assembly")?.Value;
                if (mergedAssemblyNames.Contains(assemblyName))
                {
                    // substitute <attribute assembly=""> with repack target name
                    attributeElement.AddFirst(new XComment($"original assembly='{assemblyName}'"));
                    attributeElement.SetAttributeValue("assembly", repackTargetAssemblyName);
                }

                // TODO detect type renames and update ILLink files accordingly
                /*
                var fullName = attributeElement.Attribute("fullname")?.Value;
                if (repack.RenamedTypes.TryGetValue(fullName, out var newName))
                {
                    // substitute <attribute fullname=""> with new name
                    attributeElement.SetAttributeValue("fullname", newName);
                }
                */
            }


            void UpdateType(XElement typeElement)
            {
                /*
                // TODO detect type renames and update ILLink files accordingly
                var fullName = typeElement.Attribute("fullname")?.Value;
                if (repack.RenamedTypes.TryGetValue(fullName, out var newName))
                {
                    // substitute <type fullname=""> with new name
                    typeElement.SetAttributeValue("fullname", newName);
                }
                */
                foreach (var attributeElement in typeElement.Elements("attribute"))
                {
                    UpdateAttribute(attributeElement);
                }

                foreach (var methodElement in typeElement.Elements("method"))
                {
                    UpdateMethod(methodElement);
                }

                foreach (var eventElement in typeElement.Elements("event"))
                {
                    UpdateEvent(eventElement);
                }

                foreach (var subTypeElement in typeElement.Elements("type"))
                {
                    UpdateType(subTypeElement);
                }
            }


            void UpdateMethod(XElement methodElement)
            {
                // TODO update method signature according to renames(?)

                foreach (var attribute in methodElement.Elements("attribute"))
                {
                    UpdateAttribute(attribute);
                }

                foreach (var parameter in methodElement.Elements("parameter"))
                {
                    UpdateParameter(parameter);
                }

                var returnParameter = methodElement.Element("return");
                if (returnParameter != null)
                {
                    UpdateParameter(returnParameter);
                }
            }


            void UpdateEvent(XElement eventElement)
            {
                foreach (var attribute in eventElement.Elements("attribute"))
                {
                    UpdateAttribute(attribute);
                }
            }


            void UpdateParameter(XElement parameterElement)
            {
                foreach (var attribute in parameterElement.Elements("attribute"))
                {
                    UpdateAttribute(attribute);
                }
            }
        }
        private static Resource MergeSuppressions(ILRepack repack, List<XDocument> list)
        {
            // NOTE: I didn't found format in documentation and any example `in the wild` so I assume it is like ILLink.LinkAttributes.xml 

            var repackTargetAssemblyName = repack.TargetAssemblyDefinition.Name.Name;
            var mergedAssemblyNames = new HashSet<string>(repack.MergedAssemblies.Select(asmDef => asmDef.Name.Name), StringComparer.Ordinal);

            var resourceStream = MergeIlLinkerDocuments(repack, list, UpdateAssembly);

            return new EmbeddedResource(SUPPRESSIONS_FILE_NAME, ManifestResourceAttributes.Public, resourceStream);


            void UpdateAssembly(XElement assemblyElement)
            {
                // substitute <assembly fullname=""> with repack target name
                assemblyElement.AddFirst(new XComment($" original {assemblyElement.Attribute("fullname")}"));
                assemblyElement.SetAttributeValue("fullname", repackTargetAssemblyName);

                foreach (var attributeElement in assemblyElement.Elements("attribute"))
                {
                    UpdateAttribute(attributeElement);
                }

                foreach (var typeElement in assemblyElement.Elements("type"))
                {
                    UpdateType(typeElement);
                }
            }


            void UpdateAttribute(XElement attributeElement)
            {
                var assemblyName = attributeElement.Attribute("assembly")?.Value;
                if (mergedAssemblyNames.Contains(assemblyName))
                {
                    // substitute <attribute assembly=""> with repack target name
                    attributeElement.AddFirst(new XComment($"original assembly='{assemblyName}'"));
                    attributeElement.SetAttributeValue("assembly", repackTargetAssemblyName);
                }

                // TODO detect type renames and update ILLink files accordingly
                /*
                var fullName = attributeElement.Attribute("fullname")?.Value;
                if (repack.RenamedTypes.TryGetValue(fullName, out var newName))
                {
                    // substitute <attribute fullname=""> with new name
                    attributeElement.SetAttributeValue("fullname", newName);
                }
                */
            }


            void UpdateType(XElement typeElement)
            {
                /*
                // TODO detect type renames and update ILLink files accordingly
                var fullName = typeElement.Attribute("fullname")?.Value;
                if (repack.RenamedTypes.TryGetValue(fullName, out var newName))
                {
                    // substitute <type fullname=""> with new name
                    typeElement.SetAttributeValue("fullname", newName);
                }
                */
                foreach (var attributeElement in typeElement.Elements("attribute"))
                {
                    UpdateAttribute(attributeElement);
                }

                foreach (var methodElement in typeElement.Elements("method"))
                {
                    UpdateMethod(methodElement);
                }

                foreach (var eventElement in typeElement.Elements("event"))
                {
                    UpdateEvent(eventElement);
                }

                foreach (var subTypeElement in typeElement.Elements("type"))
                {
                    UpdateType(subTypeElement);
                }
            }


            void UpdateMethod(XElement methodElement)
            {
                // TODO update method signature according to renames(?)

                foreach (var attribute in methodElement.Elements("attribute"))
                {
                    UpdateAttribute(attribute);
                }

                foreach (var parameter in methodElement.Elements("parameter"))
                {
                    UpdateParameter(parameter);
                }

                var returnParameter = methodElement.Element("return");
                if (returnParameter != null)
                {
                    UpdateParameter(returnParameter);
                }
            }


            void UpdateEvent(XElement eventElement)
            {
                foreach (var attribute in eventElement.Elements("attribute"))
                {
                    UpdateAttribute(attribute);
                }
            }


            void UpdateParameter(XElement parameterElement)
            {
                foreach (var attribute in parameterElement.Elements("attribute"))
                {
                    UpdateAttribute(attribute);
                }
            }
        }
        private static Resource MergeDescriptors(ILRepack repack, List<XDocument> list)
        {
            var repackTargetAssemblyName = repack.TargetAssemblyDefinition.Name.Name;
            var resourceStream = MergeIlLinkerDocuments(repack, list, UpdateAssembly);

            return new EmbeddedResource(DESCRIPTORS_FILE_NAME, ManifestResourceAttributes.Public, resourceStream);


            void UpdateAssembly(XElement assemblyElement)
            {
                // substitute <assembly fullname=""> with repack target name 
                assemblyElement.AddFirst(new XComment($" original {assemblyElement.Attribute("fullname")}"));
                assemblyElement.SetAttributeValue("fullname", repackTargetAssemblyName);

                foreach (var typeElement in assemblyElement.Elements("type"))
                {
                    UpdateType(typeElement);
                }
            }


            void UpdateType(XElement typeElement)
            {
                /*
                // TODO detect type renames and update ILLink files accordingly
                var fullName = typeElement.Attribute("fullname")?.Value;
                if (repack.RenamedTypes.TryGetValue(fullName, out var newName))
                {
                    // substitute <type fullname=""> with new name
                    typeElement.SetAttributeValue("fullname", newName);
                }
                */
            }
        }
        private static Resource MergeSubstitutions(ILRepack repack, List<XDocument> list)
        {
            var repackTargetAssemblyName = repack.TargetAssemblyDefinition.Name.Name;
            var resourceStream = MergeIlLinkerDocuments(repack, list, UpdateAssembly);

            return new EmbeddedResource(SUBSTITUTIONS_FILE_NAME, ManifestResourceAttributes.Public, resourceStream);


            void UpdateAssembly(XElement assemblyElement)
            {
                // substitute <assembly fullname=""> with repack target name 
                assemblyElement.AddFirst(new XComment($" original {assemblyElement.Attribute("fullname")}"));
                assemblyElement.SetAttributeValue("fullname", repackTargetAssemblyName);

                foreach (var typeElement in assemblyElement.Elements("type"))
                {
                    UpdateType(typeElement);
                }
            }


            void UpdateType(XElement typeElement)
            {
                /*
                // TODO detect type renames and update ILLink files accordingly
                var fullName = typeElement.Attribute("fullname")?.Value;
                if (repack.RenamedTypes.TryGetValue(fullName, out var newName))
                {
                    // substitute <type fullname=""> with new name
                    typeElement.SetAttributeValue("fullname", newName);
                }
                */
            }
        }

        private static Stream MergeIlLinkerDocuments(ILRepack repack, List<XDocument> list, Action<XElement> visitAssembly)
        {
            var mergedAssemblyNames = new HashSet<string>(repack.MergedAssemblies.Select(asmDef => asmDef.Name.Name), StringComparer.Ordinal);

            var document = new XDocument();
            var linker = new XElement("linker");
            document.Add(linker);
            foreach (var otherDocument in list)
            {
                foreach (var assemblyElement in otherDocument.XPathSelectElements("/linker/assembly"))
                {
                    var featureAssemblyName = assemblyElement.Attribute("fullname")?.Value;

                    if (!mergedAssemblyNames.Contains(featureAssemblyName))
                    {
                        // unknown assembly name -> add unchanged
                        linker.Add(assemblyElement);
                        continue;
                    }

                    visitAssembly(assemblyElement);
                    linker.Add(assemblyElement);
                }
            }

            var resourceStream = new MemoryStream();
            document.Save(resourceStream);
            resourceStream.Position = 0;
            return resourceStream;
        }
        private static void AddLinkerDocument(List<XDocument> list, EmbeddedResource resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (list == null) throw new ArgumentNullException(nameof(list));

            var resourceStream = resource.GetResourceStream();
            resourceStream.Position = 0;
            var document = XDocument.Load(resourceStream, LoadOptions.None);
            list.Add(document);
        }
        private static void AddOrReplaceResource(Collection<Resource> resources, string resourceName, Resource resource)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            if (resourceName == null) throw new ArgumentNullException(nameof(resourceName));
            if (resource == null) throw new ArgumentNullException(nameof(resource));

            var existingResource = resources.FirstOrDefault(r => r.Name == resourceName);
            if (existingResource != null)
            {
                resources.Remove(existingResource);
            }

            resources.Add(resource);
        }
    }
}