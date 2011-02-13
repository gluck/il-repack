using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Mono.Cecil;

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
                    if (File.Exists(assemblyConfig))
                    {
                        var doc = new XmlDocument();
                        doc.Load(assemblyConfig);
                        validConfigFiles.Add(assemblyConfig);
                    }
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
                dataset.WriteXml(repack.OutputFile + ".config");
            }
            catch (Exception e)
            {
                repack.ERROR("Failed to merge configuration files: " + e);
            }
        }
    }
}
