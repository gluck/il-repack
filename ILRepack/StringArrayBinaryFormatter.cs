using System.IO;

namespace ILRepacking
{
    /// <summary>
    /// Since BinaryFormatter was deprecated we need a replacement.
    /// However we still have the bytes produced by BinaryFormatter in the wild,
    /// embedded as resources in the assemblies we repack.
    /// To preserve compatibility with the existing bytes out there, we need
    /// to be able to read and write the same bytes as the BinaryFormatter.
    /// Fortunately we only serialize string arrays, so it's easy to reverse engineer
    /// what the binary formatter did and do the same thing using raw BinaryReader/Writer.
    /// </summary>
    internal class StringArrayBinaryFormatter
    {
        private static byte[] header = new byte[]
        {
            0x00, // binaryHeaderEnum
            0x01, 0x00, 0x00, 0x00, // topId 1
            0xff, 0xff, 0xff, 0xff, // headerId -1
            0x01, 0x00, 0x00, 0x00, // binaryFormatterMajorVersion 1
            0x00, 0x00, 0x00, 0x00, // binaryFormatterMinorVersion 0
        };

        public static byte[] Serialize(string[] array)
        {
            var stream = new MemoryStream();
            Serialize(stream, array);
            return stream.ToArray();
        }

        public static void Serialize(Stream stream, string[] array)
        {
            var binaryWriter = new BinaryWriter(stream);
            Serialize(binaryWriter, array);
        }

        public static void Serialize(BinaryWriter writer, string[] array)
        {
            int objectId = 1;

            writer.Write(header);

            writer.Write((byte)0x11); // BinaryHeaderEnum.ArraySingleString
            writer.Write(objectId++);
            writer.Write(array.Length);

            for (int i = 0; i < array.Length; i++)
            {
                writer.Write((byte)0x06); // WriteKnownValueClass WriteString
                writer.Write(objectId++);
                writer.Write(array[i]);
            }

            writer.Write((byte)0x0b);
        }

        public static string[] Deserialize(byte[] bytes)
        {
            var stream = new MemoryStream(bytes);
            return Deserialize(stream);
        }

        public static string[] Deserialize(Stream stream)
        {
            var reader = new BinaryReader(stream);
            return Deserialize(reader);
        }

        public static string[] Deserialize(BinaryReader reader)
        {
            for (int i = 0; i < 17 + 1 + 4; i++)
            {
                reader.ReadByte();
            }

            int length = reader.ReadInt32();
            string[] array = new string[length];

            for (int i = 0; i < length; i++)
            {
                reader.ReadByte(); // 0x06
                reader.ReadInt32(); // object id
                string str = reader.ReadString();
                array[i] = str;
            }

            reader.ReadByte();

            return array;
        }
    }
}