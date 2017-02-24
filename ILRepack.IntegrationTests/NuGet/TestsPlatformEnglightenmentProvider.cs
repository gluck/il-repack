using System;
using System.Reactive.PlatformServices;

namespace ILRepack.IntegrationTests.NuGet
{
    public class TestsPlatformEnglightenmentProvider : CurrentPlatformEnlightenmentProvider
    {
        public override T GetService<T>(object[] args)
        {
            if (typeof(T) == typeof(IExceptionServices))
            {
                return new WrappedExceptionServices() as T;
            }
            return base.GetService<T>(args);
        }

        internal class WrappedExceptionServices : IExceptionServices
        {
            public void Rethrow(Exception exception)
            {
                throw new Exception("Error in RX Sequence", exception);
            }
        }
    }
}