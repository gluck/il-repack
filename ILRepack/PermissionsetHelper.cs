//
// Copyright (c) 2011 Simon Goldschmidt
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
using System.Linq;
using System.Text;
using System.Xml;

using Mono.Cecil;
using Mono.Collections.Generic;

namespace ILRepacking
{
    internal class PermissionsetHelper
    {
        private static TypeReference GetTypeRef(string nameSpace, string name, string assemblyName, ModuleDefinition targetModule)
        {
            TypeReference typeRef = targetModule.ImportReference(new TypeReference(nameSpace, name, targetModule,
                    targetModule.AssemblyReferences.First(x => x.Name == assemblyName)));
            return typeRef;
        }

        public static bool IsXmlPermissionSet(SecurityDeclaration xmlDeclaration)
        {
            if (!xmlDeclaration.HasSecurityAttributes || xmlDeclaration.SecurityAttributes.Count == 0)
                // nothing to convert
                return false;
            if (xmlDeclaration.SecurityAttributes.Count > 1)
                return false;

            SecurityAttribute sa = xmlDeclaration.SecurityAttributes[0];
            if (sa.HasFields)
                return false;
            if (!sa.HasProperties || sa.Properties.Count > 1)
                return false;
            CustomAttributeNamedArgument arg = sa.Properties[0];
            if (arg.Name != "XML" || arg.Argument.Type.FullName != "System.String")
                return false;
            return true;
        }

        public static SecurityDeclaration Permission2XmlSet(SecurityDeclaration declaration, ModuleDefinition targetModule)
        {
            if (!declaration.HasSecurityAttributes || declaration.SecurityAttributes.Count == 0)
                // nothing to convert
                return declaration;
            if (declaration.SecurityAttributes.Count > 1)
                throw new Exception("Cannot convert SecurityDeclaration with more than one attribute");

            SecurityAttribute sa = declaration.SecurityAttributes[0];
            if (sa.HasFields)
                throw new NotSupportedException("Cannot convert SecurityDeclaration with fields");

            TypeReference attrType = sa.AttributeType;
            AssemblyNameReference attrAsm = (AssemblyNameReference)attrType.Scope;
            string className = attrType.FullName + ", " + attrAsm.FullName;

            XmlDocument xmlDoc = new XmlDocument();

            XmlElement permissionSet = xmlDoc.CreateElement("PermissionSet");
            permissionSet.SetAttribute("class", "System.Security.PermissionSet");
            permissionSet.SetAttribute("version", "1");

            XmlElement iPermission = xmlDoc.CreateElement("IPermission");
            iPermission.SetAttribute("class", className);
            iPermission.SetAttribute("version", "1");
            foreach (var arg in sa.Properties)
            {
                iPermission.SetAttribute(arg.Name, arg.Argument.Value.ToString());
            }

            permissionSet.AppendChild(iPermission);
            xmlDoc.AppendChild(permissionSet);

            SecurityDeclaration xmlDeclaration = new SecurityDeclaration(declaration.Action);
            SecurityAttribute attribute = new SecurityAttribute(GetTypeRef("System.Security.Permissions", "PermissionSetAttribute", "mscorlib", targetModule));

            attribute.Properties.Add(new CustomAttributeNamedArgument("XML",
                new CustomAttributeArgument(targetModule.TypeSystem.String, xmlDoc.InnerXml)));

            xmlDeclaration.SecurityAttributes.Add(attribute);
            return xmlDeclaration;
        }

        public static SecurityDeclaration Xml2PermissionSet(SecurityDeclaration xmlDeclaration, ModuleDefinition targetModule)
        {
            if (!xmlDeclaration.HasSecurityAttributes || xmlDeclaration.SecurityAttributes.Count == 0)
                // nothing to convert
                return null;
            if (xmlDeclaration.SecurityAttributes.Count > 1)
                throw new Exception("Cannot convert SecurityDeclaration with more than one attribute");

            SecurityAttribute sa = xmlDeclaration.SecurityAttributes[0];
            if (sa.HasFields)
                throw new NotSupportedException("Cannot convert SecurityDeclaration with fields");
            if (!sa.HasProperties || sa.Properties.Count > 1)
                throw new NotSupportedException("Invalid XML SecurityDeclaration (only 1 property supported)");
            CustomAttributeNamedArgument arg = sa.Properties[0];
            if (arg.Name != "XML" || arg.Argument.Type.FullName != "System.String")
                throw new ArgumentException("Property \"XML\" expected");
            if (string.IsNullOrEmpty(arg.Argument.Value as string))
                return null;
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml((string)arg.Argument.Value);
            XmlNode permissionSet = xmlDoc.SelectSingleNode("/PermissionSet");
            if (permissionSet == null)
                return null;
            XmlNode permissionSetClass = permissionSet.SelectSingleNode("@class"); // check version?
            if (permissionSetClass == null)
                return null;
            if (permissionSetClass.Value != "System.Security.PermissionSet")
                return null;
            XmlNode iPermission = permissionSet.SelectSingleNode("IPermission");
            if (iPermission == null)
                return null;
            XmlNode iPermissionClass = iPermission.SelectSingleNode("@class"); // check version?
            if (iPermissionClass == null)
                return null;

            // Create Namespace & Name from FullName, AssemblyName can be ignored since we look it up.
            string[] valueParts = iPermissionClass.Value.Split(',');
            Collection<string> classNamespace = new Collection<string>(valueParts[0].Split('.'));
            string assemblyName = valueParts[1].Trim();
            string className = classNamespace[classNamespace.Count - 1];
            classNamespace.RemoveAt(classNamespace.Count - 1);
            SecurityAttribute attribute = new SecurityAttribute(GetTypeRef(string.Join(".", classNamespace.ToArray()), className, assemblyName, targetModule));
            foreach (XmlAttribute xmlAttr in iPermission.Attributes)
            {
                if ((xmlAttr.Name != "class") && (xmlAttr.Name != "version"))
                {
                    attribute.Properties.Add(new CustomAttributeNamedArgument(xmlAttr.Name,
                        new CustomAttributeArgument(targetModule.TypeSystem.String, xmlAttr.Value)));
                }
            }
            SecurityDeclaration newSd = new SecurityDeclaration(xmlDeclaration.Action);
            newSd.SecurityAttributes.Add(attribute);
            return newSd;
        }
    }
}
