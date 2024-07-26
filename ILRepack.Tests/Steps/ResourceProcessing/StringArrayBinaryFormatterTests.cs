using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using ILRepacking;
using NUnit.Framework;

namespace ILRepack.Tests.Steps.ResourceProcessing
{
    internal class StringArrayBinaryFormatterTests
    {
        [Test]
        public void StringArrayRoundtrip()
        {
            var cases = new[]
            {
                Array.Empty<string>(),
                new [] { "" },
                new [] { "a" },
                new [] { "ab" },
                new [] { "a", "b" },
                new [] { "", "b" },
                new [] { "a", "" },
                new [] { "\r\n", "\t" },
                new [] { "🌸", "ꙮ", "Assembly.Name, Version=1.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a" },
            };

            foreach (var stringArray in cases)
            {
                var bytes = StringArrayBinaryFormatter.Serialize(stringArray);
                var back = StringArrayBinaryFormatter.Deserialize(bytes);
                Assert.True(Enumerable.SequenceEqual(stringArray, back));

                var stream = new MemoryStream();
#pragma warning disable SYSLIB0011 // Type or member is obsolete
                new BinaryFormatter().Serialize(stream, stringArray);
#pragma warning restore SYSLIB0011 // Type or member is obsolete
                var oracleBytes = stream.ToArray();

                Assert.True(Enumerable.SequenceEqual(bytes, oracleBytes));
            }
        }
    }
}