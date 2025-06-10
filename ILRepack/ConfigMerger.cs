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
using System.Linq;
using System.Xml.Linq;

namespace ILRepacking
{
    internal static class ConfigMerger
    {
        internal static void Process(ILRepack repack)
        {
            try
            {
                var validConfigFiles = new List<XDocument>();
                foreach (string assembly in repack.MergedAssemblyFiles)
                {
                    string assemblyConfig = assembly + ".config";
                    if (!File.Exists(assemblyConfig))
                        continue;
                    var doc = XDocument.Load(assemblyConfig);
                    validConfigFiles.Add(doc);
                }

                if (validConfigFiles.Count == 0)
                    return;

                repack.Logger.Verbose($"Merging config files: {string.Join(",", validConfigFiles)}...");

                var firstFile = validConfigFiles[0];
                validConfigFiles.RemoveAt(0);
                foreach (var configFile in validConfigFiles)
                {
                    MergeElements(firstFile.Root, configFile.Root, new());
                }

                CleanupMultipleStartups(firstFile.Root);

                var mergedAssemblies = new HashSet<string>(repack.MergedAssemblies.Select(a => a.Name.Name));

                CleanupRedirectedAssemblies(firstFile.Root, mergedAssemblies);

                firstFile.Save(repack.Options.OutputFile + ".config");
            }
            catch (Exception e)
            {
                repack.Logger.Error("Failed to merge configuration files: " + e);
            }
        }

        private static void CleanupMultipleStartups(XElement root)
        {
            var startups = root.Elements("startup");

            foreach (var s in startups.Skip(1).ToArray())
            {
                s.Remove();
            }
        }

        private static void CleanupRedirectedAssemblies(XElement root, HashSet<string> mergedAssemblies)
        {
            var runtime = root.Element("runtime");
            if (runtime == null)
                return;

            var ns = "urn:schemas-microsoft-com:asm.v1";

            var bindings = runtime.Elements(XName.Get("assemblyBinding", ns)).ToArray();

            foreach (var binding in bindings)
            {
                var dependents = binding.Elements(XName.Get("dependentAssembly", ns)).ToArray();

                foreach (var dependent in dependents)
                {
                    var identity = dependent.Element(XName.Get("assemblyIdentity", ns));

                    if (identity == null) continue;

                    var name = identity.Attribute("name");

                    if (mergedAssemblies.Contains(name.Value))
                    {
                        dependent.Remove();
                    }
                }

                if (!binding.Elements().Any())
                {
                    binding.Remove();
                }
            }
        }

        // determine which elements we consider the same
        private static bool ShouldMerge(XElement a, XElement b)
        {
            if (a.Name == XName.Get("assemblyBinding", "urn:schemas-microsoft-com:asm.v1"))
            {
                return false;
            }

            return SameElement(a, b);
        }

        private static bool SameElement(XElement a, XElement b)
        {
            if (a.Name != b.Name) return false;
            if (!a.HasAttributes && !b.HasAttributes) return true;
            if (!a.HasAttributes || !b.HasAttributes) return false;
            if (a.Attributes().Count() != b.Attributes().Count()) return false;

            return a.Attributes().All(attA => b.Attributes(attA.Name)
                .Count(attB => attB.Value == attA.Value) != 0);
        }

        // Merge "merged" document B into "source" A
        private static void MergeElements(XElement parentA, XElement parentB, HashSet<XElement> state)
        {
            // merge per-element content from parentB into parentA
            //
            foreach (XNode nodeB in parentB.DescendantNodes())
            {
                if (nodeB is not XElement childB) continue;

                if (state.Contains(childB)) continue;

                // merge childB with first equivalent childA
                // equivalent childB1, childB2,.. will be combined
                //
                bool isMatchFound = false;
                foreach (XElement childA in parentA.Descendants())
                {
                    if (ShouldMerge(childA, childB))
                    {
                        MergeElements(childA, childB, state);
                        isMatchFound = true;
                        break;
                    }
                }

                // if there is no equivalent childA, add childB into parentA
                //
                if (isMatchFound)
                    return;

                parentA.Add(childB);

                foreach (var e in childB.DescendantsAndSelf())
                {
                    state.Add(e);
                }
            }
        }
    }
}