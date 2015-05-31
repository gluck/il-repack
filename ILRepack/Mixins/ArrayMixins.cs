using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ILRepacking.Mixins
{
    internal static class ArrayMixins
    {
        public static T[] Clone<T>(this T[] input, Func<T, T> transform)
        {
            var ret = new T[input.Length];
            for (int i = 0; i < input.Length; i++)
                ret[i] = transform(input[i]);
            return ret;
        }
    }
}
