using ILRepacking;
using NUnit.Framework;

namespace ILRepack.Tests
{
    [TestFixture]
    class RepackLoggerTests
    {
        [Test]
        public void GivenAnEmptyOutputFile__OpenFile_LogLine__OpenReturnsFalse_LogDoesNotThrowError()
        {
            RepackLogger logger = new RepackLogger();
            Assert.IsFalse(logger.Open(""));
            logger.Info("Hello");
        }

        [Test]
        public void GivenAnEmptyOutputFile__OpenFile_CloseFile__OpenReturnsFalse_ClosedDoesNotThrowError()
        {
            RepackLogger logger = new RepackLogger();
            Assert.IsFalse(logger.Open(""));
            logger.Close();
        }

        [Test]
        public void GivenOutputFile__OpenFile_CloseFile_LogError__StreamIsOpened_StreamIsClosed_NoErrorIsThrown()
        {
            RepackLogger logger = new RepackLogger();
            Assert.IsTrue(logger.Open("file.out"));
            logger.Close();
            const string message = "Only written to the console. No error is thrown.";
            logger.Error(message);
            logger.Warn(message);
            logger.Info(message);
            logger.Verbose(message);
        }
    }
}
