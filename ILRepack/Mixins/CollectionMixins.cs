using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ILRepacking.Mixins
{
    static class CollectionMixins
    {
        public static void AddRange<T>(this Collection<T> dest, IEnumerable<T> source)
        {
            foreach (var obj in source) dest.Add(obj);
        }
    }
}
