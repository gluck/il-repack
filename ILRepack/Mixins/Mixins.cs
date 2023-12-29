using System.Collections.Generic;
using Mono.Collections.Generic;

namespace ILRepacking.Mixins
{
    static class CollectionMixins
    {
        public static void AddRange<T>(this Collection<T> destination, IEnumerable<T> source)
        {
            foreach (var item in source)
            {
                destination.Add(item);
            }
        }
    }
}