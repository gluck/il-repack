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
using System.Linq;
using System.Text;

namespace ILRepacking
{
    public class CommandLine : ICommandLine
    {
        private readonly List<string> parameters;

        public CommandLine(IEnumerable<string> args)
        {
            parameters = new List<string>(args);
        }

        public string[] OtherAguments
        {
            get
            {
                return parameters.ToArray();
            }
        }

        public bool Modifier(string modifier)
        {
            return parameters.RemoveAll(x =>
                StringComparer.InvariantCultureIgnoreCase.Equals(x, "/" + modifier) ||
                StringComparer.InvariantCultureIgnoreCase.Equals(x, "-" + modifier) ||
                StringComparer.InvariantCultureIgnoreCase.Equals(x, "--" + modifier)) > 0;
        }

        public string Option(string name)
        {
            var ret = OptionFinder(name, parameters.FirstOrDefault(x => OptionFinder(name, x) != null));
            parameters.RemoveAll(x => OptionFinder(name, x) != null);
            return ret;
        }

        public bool HasOption(string name)
        {
            return parameters.Any(x =>
                x.ToLower().StartsWith("/" + name) ||
                x.ToLower().StartsWith("-" + name) ||
                x.ToLower().StartsWith("--" + name));
        }

        public string[] Options(string name)
        {
            var ret = parameters.Select(x => OptionFinder(name, x)).Where(x=>x!=null).ToArray();
            parameters.RemoveAll(x => OptionFinder(name, x) != null);
            return ret;
        }

        public bool OptionBoolean(string name, bool def)
        {
            string val = Option(name);
            return val == null ? def : StringComparer.InvariantCultureIgnoreCase.Equals(val, "true");
        }
        
        public int OptionsCount 
        {
            get { return parameters.Count; }
        }

        public bool HasNoOptions
        {
            get { return OptionsCount == 0; }
        }

        private static string OptionFinder(string option, string param)
        {
            if (string.IsNullOrEmpty(param) || param.Length < option.Length + 1)
                return null;
            if (param[0] != '/' && param[0] != '-')
                return null;
            if (param[0] == '-' && param[1] == '-' && param.Length > option.Length + 1)
                param = param.Substring(1);
            if (!StringComparer.InvariantCultureIgnoreCase.Equals(param.Substring(1, option.Length), option))
                return null;
            var ret = param.Substring(option.Length+1);
            if (ret == string.Empty)
                return string.Empty;
            if (ret[0] != ':' && ret[0] != '=')
                return null;
            return ret.Substring(1).Trim();
        }

    }
}
