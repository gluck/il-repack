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

namespace ILRepacking
{
    internal static class ConfigMerger
    {
        internal static void Process(ILRepack repack)
        {
            try
            {
                var validConfigFiles = new List<string>();
                foreach (string assembly in repack.MergedAssemblyFiles)
                {
                    string assemblyConfig = assembly + ".config";
                    if (!File.Exists(assemblyConfig))
                        continue;
                    var doc = new XmlDocument();
                    doc.Load(assemblyConfig);
                    validConfigFiles.Add(assemblyConfig);
                }

                if (validConfigFiles.Count == 0)
                    return;

                string firstFile = validConfigFiles[0];
                var dataset = new System.Data.DataSet();
                dataset.ReadXml(firstFile);
                validConfigFiles.Remove(firstFile);

                foreach (string configFile in validConfigFiles)
                {
                    var nextDataset = new System.Data.DataSet();
                    nextDataset.ReadXml(configFile);
                    dataset.Merge(nextDataset);
                }
                dataset.WriteXml(repack.Options.OutputFile + ".config");
            }
            catch (Exception e)
            {
                repack.Logger.Error("Failed to merge configuration files: " + e);
            }
        }
    }
}
