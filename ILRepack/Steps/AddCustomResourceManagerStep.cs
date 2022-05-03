using Microsoft.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.CodeDom.Compiler;
using System.Linq;
using System.Text;

namespace ILRepacking.Steps
{
    internal class AddCustomResourceManagerStep : IRepackStep
    {
        private readonly ILogger _logger;
        private readonly IRepackContext _repackContext;
        private readonly IRepackImporter _repackImporter;
        private readonly RepackOptions _options;
        private readonly ModuleDefinition _targetAssemblyMainModule;
        private MethodReference _singleAssemblyResourceManagerCtorReference;

        public AddCustomResourceManagerStep(ILogger logger, IRepackContext repackContext, IRepackImporter repackImporter, RepackOptions options)
        {
            _logger = logger;
            _options = options;
            _repackContext = repackContext;
            _repackImporter = repackImporter;
            _targetAssemblyMainModule = _repackContext.TargetAssemblyMainModule;
        }

        public void Perform()
        {
            if (!this._options.UseCustomResourceManager)
            {
                return;
            }

            _logger.Info("Adding custom resource manager");

            // create customResourceManagerAssemblyModule and inject into assembly.
            CompileILRepackCustomResourceManagerAndLoadToAssembly();

            var assembliesList = _repackContext.OtherAssemblies.Concat(new[] { _repackContext.PrimaryAssemblyDefinition });
            foreach (var assembly in assembliesList)
            {
                // support resources type and namespace renaming
                // current .net resources generator calls ResourceManager constructor with a string, instead it should be making use of a strongly typed reference to generate the string.
                // to support namespace renaming in resources a "manual" update must be made.
                foreach (var type in assembly.Modules.SelectMany(x => x.Types))
                {
                    foreach (MethodDefinition meth in type.Methods)
                    {
                        CheckTypeForResourceNamespaceRenaming(type, meth);
                    }
                }
            }
        }

        private void CompileILRepackCustomResourceManagerAndLoadToAssembly()
        {
            string customResourceManagerCode = "using System;\r\nusing System.Collections.Generic;\r\nusing System.Globalization;\r\nusing System.Reflection;\r\nusing System.Resources;\r\n\r\nnamespace ILRepack\r\n{\r\n    /// <summary>\r\n    /// SingleAssembly Resource Manager.\r\n    /// </summary>\r\n    /// <seealso cref=\"System.Resources.ResourceManager\" />\r\n    public class SingleAssemblyResourceManager : ResourceManager\r\n    {\r\n        private Dictionary<string, ResourceSet> singleAssemblyResourceSets;\r\n        private string[] singleAssemblyResouceNames;\r\n\r\n        /// <summary>\r\n        /// Initializes a new instance of the <see cref=\"SingleAssemblyResourceManager\" /> class.\r\n        /// </summary>\r\n        /// <param name=\"baseName\">The root name of the resource file without its extension but including any fully qualified namespace name. For example, the root name for the resource file named MyApplication.MyResource.en-US.resources is MyApplication.MyResource.</param>\r\n        /// <param name=\"assembly\">The main assembly for the resources.</param>\r\n        public SingleAssemblyResourceManager(string baseName, Assembly assembly)\r\n            : base(baseName, assembly)\r\n        {\r\n            this.singleAssemblyResourceSets = new Dictionary<string, ResourceSet>();\r\n            this.singleAssemblyResouceNames = this.MainAssembly.GetManifestResourceNames();\r\n        }\r\n\r\n        /// <summary>\r\n        /// Internals the get resource set.\r\n        /// </summary>\r\n        /// <param name=\"culture\">The culture.</param>\r\n        /// <param name=\"createIfNotExists\">if set to <c>true</c> [create if not exists].</param>\r\n        /// <param name=\"tryParents\">if set to <c>true</c> [try parents].</param>\r\n        /// <returns>sample jk.</returns>\r\n        protected override ResourceSet InternalGetResourceSet(CultureInfo culture, bool createIfNotExists, bool tryParents)\r\n        {\r\n            // intentional behaviour always try to fallback to parent cultures\r\n            while (culture != null && culture != CultureInfo.InvariantCulture)\r\n            {\r\n                // Look in the Single Assembly ResourceSet table\r\n                Dictionary<string, ResourceSet> localResourceSets = this.singleAssemblyResourceSets;\r\n                ResourceSet rs = null;\r\n\r\n                if (localResourceSets != null)\r\n                {\r\n                    lock (localResourceSets)\r\n                    {\r\n                        localResourceSets.TryGetValue(culture.Name, out rs);\r\n                    }\r\n                }\r\n\r\n                if (rs != null)\r\n                {\r\n                    return rs;\r\n                }\r\n\r\n                var cultureNameResource = this.FindResourceNameIgnoreCase(this.BaseName + \".\" + culture.Name + \".resources\");\r\n\r\n                if (!string.IsNullOrWhiteSpace(cultureNameResource))\r\n                {\r\n                    var resourceStream = this.MainAssembly.GetManifestResourceStream(cultureNameResource);\r\n\r\n                    if (resourceStream != null)\r\n                    {\r\n                        rs = new ResourceSet(resourceStream);\r\n                        AddResourceSet(localResourceSets, culture.Name, ref rs);\r\n                        return rs;\r\n                    }\r\n                }\r\n\r\n                culture = culture.Parent;\r\n            }\r\n\r\n            var resSet = base.InternalGetResourceSet(culture, createIfNotExists, tryParents);\r\n\r\n            return resSet;\r\n        }\r\n\r\n        // Simple helper to ease maintenance and improve readability.\r\n        private string FindResourceNameIgnoreCase(string resourceName)\r\n        {\r\n            foreach (var item in this.singleAssemblyResouceNames)\r\n            {\r\n                if (item.Equals(resourceName, StringComparison.OrdinalIgnoreCase))\r\n                {\r\n                    return item;\r\n                }\r\n            }\r\n\r\n            return string.Empty;\r\n        }\r\n\r\n        // Simple helper to ease maintenance and improve readability.\r\n        private static void AddResourceSet(Dictionary<string, ResourceSet> localResourceSets, string cultureName, ref ResourceSet rs)\r\n        {\r\n            // InternalGetResourceSet is both recursive and reentrant - \r\n            // assembly load callbacks in particular are a way we can call\r\n            // back into the ResourceManager in unexpectedly on the same thread.\r\n            lock (localResourceSets)\r\n            {\r\n                // If another thread added this culture, return that.\r\n                ResourceSet lostRace;\r\n\r\n                if (localResourceSets.TryGetValue(cultureName, out lostRace))\r\n                {\r\n                    if (!object.ReferenceEquals(lostRace, rs))\r\n                    {\r\n                        // Note: In certain cases, we can be trying to add a ResourceSet for multiple\r\n                        // cultures on one thread, while a second thread added another ResourceSet for one\r\n                        // of those cultures.  If there is a race condition we must make sure our ResourceSet \r\n                        // isn't in our dictionary before closing it.\r\n                        if (!localResourceSets.ContainsValue(rs))\r\n                        {\r\n                            rs.Dispose();\r\n                        }\r\n\r\n                        rs = lostRace;\r\n                    }\r\n                }\r\n                else\r\n                {\r\n                    localResourceSets.Add(cultureName, rs);\r\n                }\r\n            }\r\n        }\r\n\r\n        /// <summary>\r\n        /// Tells the resource manager to call the <see cref=\"M:System.Resources.ResourceSet.Close\"></see> method on all <see cref=\"T:System.Resources.ResourceSet\"></see> objects and release all resources.\r\n        /// </summary>\r\n        public override void ReleaseAllResources()\r\n        {\r\n            base.ReleaseAllResources();\r\n\r\n            Dictionary<string, ResourceSet> localResourceSets = this.singleAssemblyResourceSets;\r\n\r\n            // If any calls to Close throw, at least leave ourselves in a\r\n            // consistent state.\r\n            this.singleAssemblyResourceSets = new Dictionary<string, ResourceSet>();\r\n\r\n            lock (localResourceSets)\r\n            {\r\n                foreach (var item in localResourceSets)\r\n                {\r\n                    item.Value.Close();\r\n                }\r\n            }\r\n        }\r\n\r\n        /// <summary>\r\n        /// Returns the value of the string resource localized for the specified culture.\r\n        /// </summary>\r\n        /// <param name=\"name\">The name of the resource to retrieve.</param>\r\n        /// <param name=\"culture\">An object that represents the culture for which the resource is localized.</param>\r\n        /// <returns>\r\n        /// The value of the resource localized for the specified culture, or null if <paramref name=\"name\">name</paramref> cannot be found in a resource set.\r\n        /// </returns>\r\n        public override string GetString(string name, CultureInfo culture)\r\n        {\r\n            return base.GetString(name, culture);\r\n        }\r\n    }\r\n}\r\n";

            CSharpCodeProvider provider = new CSharpCodeProvider();
            CompilerParameters parameters = new CompilerParameters
            {
                GenerateInMemory = true,
                GenerateExecutable = false,
                OutputAssembly = AppDomain.CurrentDomain.BaseDirectory + "SingleAssemblyResourceManager.dll"
            };

            CompilerResults results = provider.CompileAssemblyFromSource(parameters, customResourceManagerCode);

            if (results.Errors.HasErrors)
            {
                StringBuilder sb = new StringBuilder();

                foreach (CompilerError error in results.Errors)
                {
                    sb.AppendLine(String.Format("Error ({0}): {1}", error.ErrorNumber, error.ErrorText));
                }

                throw new InvalidOperationException(sb.ToString());
            }

            if (results.CompiledAssembly != null)
            {
                var customResourceManagerAssemblyModule = AssemblyDefinition.ReadAssembly(parameters.OutputAssembly);

                // get ILRepack.SingleAssemblyResourceManager  Custom Resource Manager type
                var typeRef = customResourceManagerAssemblyModule.MainModule.GetTypes().LastOrDefault();

                // add to output assembly
                _repackImporter.Import(typeRef, _repackContext.TargetAssemblyMainModule.Types, true, false);

                // keep ILRepack.SingleAssemblyResourceManager .ctor Method ref for replace of default Resources Manager.
                this._singleAssemblyResourceManagerCtorReference = typeRef.Methods.Where(m => m.Name.Equals(".ctor", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            }
        }

        /// <summary>
        /// Checks the type for resource namespace renaming. Support resources type and namespace renaming
        /// Currently (2/2020) afaik .Net Core resources generator calls ResourceManager constructor with a string. 
        /// Instead it should be making use of a strongly typed reference to generate this same string.
        /// to support namespace renaming in resources a "manual" update must be made.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="meth">The method.</param>
        private void CheckTypeForResourceNamespaceRenaming(TypeDefinition type, MethodDefinition meth)
        {
            if (this._options.RenameNameSpaces && type.FullName.EndsWith("Resources") && meth.Name.Equals("get_ResourceManager"))
            {
                // is there a ResourceManager ctor instruction?
                var resourceManagerConscrutorIntruction = meth.Body.Instructions.FirstOrDefault(i => i.OpCode.Code == Code.Newobj && ((dynamic)i.Operand).DeclaringType.FullName == typeof(System.Resources.ResourceManager).FullName);

                if (resourceManagerConscrutorIntruction != null)
                {
                    // type rename instruction.

                    if (meth.Body.Instructions.FirstOrDefault(i => i.OpCode.Code == Code.Ldstr && ((string)i.Operand) == type.FullName) is Instruction instruction)
                    {
                        foreach (var namespacesToReplace in this._options.RenameNameSpacesMatches)
                        {
                            if (namespacesToReplace.Key.IsMatch((string)instruction.Operand))
                            {
                                instruction.Operand = namespacesToReplace.Key.Replace((string)instruction.Operand, namespacesToReplace.Value);

                                break;
                            }
                        }
                    }

                    // replace resource manager;
                    resourceManagerConscrutorIntruction.Operand = this._singleAssemblyResourceManagerCtorReference;
                }
            }
        }
    }
}
