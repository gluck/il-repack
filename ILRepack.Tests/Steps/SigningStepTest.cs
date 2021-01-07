using ILRepacking.Steps;
using ILRepacking.Steps.SourceServerData;
using NUnit.Framework;

namespace ILRepack.Tests.Steps
{
    internal class SigningStepTest
    {
        [TestCase]
        public static void Validate_GetPublicKey_Resolved()
        {
            Assert.NotNull(SigningStep.GetPublicKey);
        }
    }
}
