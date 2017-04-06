using System.Text.RegularExpressions;

namespace ILRepacking.Mixins
{
    static class StringMixins
    {
        public static int IndexOfRegex(this string s, Regex regex)
        {
            var result = regex.Match(s);

            if (result.Success)
            {
                return result.Index;
            }

            return -1;
        }
    }
}
