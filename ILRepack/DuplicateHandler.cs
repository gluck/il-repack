using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using System.Diagnostics;

namespace ILRepacking
{
    class DuplicateHandler
    {
        private IDictionary<string, string> renames = new Dictionary<string, string>();
        internal TypeReference Rename(TypeReference r)
        {
            string other;
            if (renames.TryGetValue(r.FullName, out other))
            {
                r.Name = other;
            }
            return r;
        }
        internal void Reset()
        {
            renames.Clear();
        }
        internal string Get(string fullName, string name)
        {
            string other = "<" + Guid.NewGuid() + ">" + name;
            renames[fullName] = other;
            return other;
        }
    }
}
