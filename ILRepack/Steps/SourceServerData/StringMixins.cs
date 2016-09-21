using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ILRepacking.Steps.SourceServerData
{
    internal static class StringMixins
    {
        public static IEnumerable<string> GetLines(this string s)
        {
            return Regex.Split(s ?? "", "\r\n|\r|\n").Where(line => line != string.Empty);
        }
    }
}
