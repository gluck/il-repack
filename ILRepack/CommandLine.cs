using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ILRepacking
{
    internal class CommandLine
    {
        private readonly List<string> parameters;

        public CommandLine(string[] args)
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
