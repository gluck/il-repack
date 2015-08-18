using Mono.Cecil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ILRepacking.Mixins
{
    static class AssemblyNameReferenceCollectionMixins
    {
        public static AssemblyNameReference AddUniquely(this Collection<AssemblyNameReference> @this, AssemblyNameReference add)
        {
            var any = @this.FirstOrDefault(anr => Equals(anr, add));
            if (any != null)
                return any;
            @this.Add(add);
            return add;
        }

        private static bool Equals(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null)
                return false;
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        private static bool Equals<T>(T a, T b) where T : class, IEquatable<T>
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null)
                return false;
            return a.Equals(b);
        }

        private static bool Equals(AssemblyNameReference a, AssemblyNameReference b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a.Name != b.Name)
                return false;
            if (!Equals(a.Version, b.Version))
                return false;
            if (a.Culture != b.Culture)
                return false;
            if (!Equals(a.PublicKeyToken, b.PublicKeyToken))
                return false;
            // unsure about this one, but there's #41 and duplicate asm references can't really hurt
            if (a.IsRetargetable != b.IsRetargetable)
                return false;
            return true;
        }

    }
}
