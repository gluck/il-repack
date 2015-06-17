using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ILRepack.IntegrationTests.Helpers
{
    public static class XPlat
    {
        public static bool IsMono
        {
            get
            {
                return Type.GetType("Mono.Runtime") != null;
            }
        }

        public static bool IsWindows => !IsMono;
    }
}
