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
            ILogger logger = new RepackLogger();
            Assert.IsFalse(logger.Open(""));
            logger.Log("Hello");
        }

        [Test]
        public void GivenAnEmptyOutputFile__OpenFile_CloseFile__OpenReturnsFalse_ClosedDoesNotThrowError()
        {
            ILogger logger = new RepackLogger();
            Assert.IsFalse(logger.Open(""));
            logger.Close();
        }

        [Test]
        public void GivenOutputFile__OpenFile_CloseFile_LogError__StreamIsOpened_StreamIsClosed_NoErrorIsThrown()
        {
            ILogger logger = new RepackLogger();
            Assert.IsTrue(logger.Open("file.out"));
            logger.Close();
            const string message = "Only written to the console. No erorr is thrown.";
            logger.ERROR(message);
            logger.WARN(message);
            logger.VERBOSE(message);
            logger.INFO(message);
        }
    }
}
