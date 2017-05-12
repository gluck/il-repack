using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ILRepacking.Steps.SourceServerData;
using NUnit.Framework;

namespace ILRepack.Tests
{
    [TestFixture]
    class PdbStrTest
    {
        private volatile Exception _backgroundException;
        private UnhandledExceptionEventHandler _handler;

        [SetUp]
        public void Before()
        {
            _handler = (o, e) =>
            {
                _backgroundException = (Exception)e.ExceptionObject;
            };
            AppDomain.CurrentDomain.UnhandledException += _handler;
        }

        [TearDown]
        public void After()
        {
            AppDomain.CurrentDomain.UnhandledException -= _handler;
        }

        [Test]
        public void ConstructPdbStrInLoop()
        {
            for (int i = 0; i < 200; i++)
            {
                using (var pdbStr = new PdbStr())
                {
                    // allocating some memory to some presure on the GC and cause a cleanup.
                    var bytes = new byte[65536];
                    Assert.IsNull(_backgroundException, "error on iteration: {0}", i);
                }
            }
        }
    }
}
