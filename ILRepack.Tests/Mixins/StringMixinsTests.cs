using System.Text.RegularExpressions;
using ILRepacking.Mixins;
using NUnit.Framework;

namespace ILRepack.Tests.Mixins
{
    class StringMixinsTests
    {
        [Test]
        public void ShouldMatchStringWithoutSpaceAndReturnProperIndex()
        {
            var regexMe = "Internal.Assembly,PublicKey=1230948021398023948-2309";
            var regex = new Regex(", ?PublicKey=");

            var result = regexMe.IndexOfRegex(regex);

            Assert.AreEqual(17, result);
        }

        [Test]
        public void ShouldMatchStringWithSpaceAndReturnProperIndex()
        {
            var regexMe = "Internal.Assembly, PublicKey=1230948021398023948-2309";
            var regex = new Regex(", ?PublicKey=");

            var result = regexMe.IndexOfRegex(regex);

            Assert.AreEqual(17, result);
        }

        [Test]
        public void ShouldNotMatchStringThatDoesNotMatchRegex()
        {
            var regexMe = "Internal.Assembly, ublicKey=1230948021398023948-2309";
            var regex = new Regex(", ?PublicKey=");

            var result = regexMe.IndexOfRegex(regex);

            Assert.AreEqual(-1, result);
        }
    }
}
