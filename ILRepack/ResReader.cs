using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Resources;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace ILRepacking
{
    /*
     * Home-made Resource reader, that allows binary iteration over resources (without deserializing them).
     * Largely 'inspired' from MS ResourceReader
     */

    // The Default Resource File Format (from MS)
    //
    // The fundamental problems addressed by the resource file format are:
    //
    // * Versioning - A ResourceReader could in theory support many different
    // file format revisions.
    // * Storing intrinsic datatypes (ie, ints, Strings, DateTimes, etc) in a compact
    // format
    // * Support for user-defined classes - Accomplished using Serialization
    // * Resource lookups should not require loading an entire resource file - If you
    // look up a resource, we only load the value for that resource, minimizing working set.
    //
    //
    // There are four sections to the default file format.  The first
    // is the Resource Manager header, which consists of a magic number
    // that identifies this as a Resource file, and a ResourceSet class name.
    // The class name is written here to allow users to provide their own
    // implementation of a ResourceSet (and a matching ResourceReader) to
    // control policy.  If objects greater than a certain size or matching a
    // certain naming scheme shouldn't be stored in memory, users can tweak that
    // with their own subclass of ResourceSet.
    //
    // The second section in the system default file format is the
    // RuntimeResourceSet specific header.  This contains a version number for
    // the .resources file, the number of resources in this file, the number of
    // different types contained in the file, followed by a list of fully
    // qualified type names.  After this, we include an array of hash values for
    // each resource name, then an array of virtual offsets into the name section
    // of the file.  The hashes allow us to do a binary search on an array of
    // integers to find a resource name very quickly without doing many string
    // compares (except for once we find the real type, of course).  If a hash
    // matches, the index into the array of hash values is used as the index
    // into the name position array to find the name of the resource.  The type
    // table allows us to read multiple different classes from the same file,
    // including user-defined types, in a more efficient way than using
    // Serialization, at least when your .resources file contains a reasonable
    // proportion of base data types such as Strings or ints.  We use
    // Serialization for all the non-instrinsic types.
    //
    // The third section of the file is the name section.  It contains a
    // series of resource names, written out as byte-length prefixed little
    // endian Unicode strings (UTF-16).  After each name is a four byte virtual
    // offset into the data section of the file, pointing to the relevant
    // string or serialized blob for this resource name.
    //
    // The fourth section in the file is the data section, which consists
    // of a type and a blob of bytes for each item in the file.  The type is
    // an integer index into the type table.  The data is specific to that type,
    // but may be a number written in binary format, a String, or a serialized
    // Object.
    //
    // The system default file format (V1) is as follows:
    //
    //     What                                               Type of Data
    // ===================================================   ===========
    //
    //                        Resource Manager header
    // Magic Number (0xBEEFCACE)                              Int32
    // Resource Manager header version                        Int32
    // Num bytes to skip from here to get past this header    Int32
    // Class name of IResourceReader to parse this file       String
    // Class name of ResourceSet to parse this file           String
    //
    //                       RuntimeResourceReader header
    // ResourceReader version number                          Int32
    // [Only in debug V2 builds - "***DEBUG***"]              String
    // Number of resources in the file                        Int32
    // Number of types in the type table                      Int32
    // Name of each type                                      Set of Strings
    // Padding bytes for 8-byte alignment (use PAD)           Bytes (0-7)
    // Hash values for each resource name                     Int32 array, sorted
    // Virtual offset of each resource name                   Int32 array, coupled with hash values
    // Absolute location of Data section                      Int32
    //
    //                     RuntimeResourceReader Name Section
    // Name & virtual offset of each resource                 Set of (UTF-16 String, Int32) pairs
    //
    //                     RuntimeResourceReader Data Section
    // Type and Value of each resource                Set of (Int32, blob of bytes) pairs
    //
    // This implementation, when used with the default ResourceReader class,
    // loads only the strings that you look up for.  It can do string comparisons
    // without having to create a new String instance due to some memory mapped
    // file optimizations in the ResourceReader and FastResourceComparer
    // classes.  This keeps the memory we touch to a minimum when loading
    // resources.
    //
    // If you use a different IResourceReader class to read a file, or if you
    // do case-insensitive lookups (and the case-sensitive lookup fails) then
    // we will load all the names of each resource and each resource value.
    // This could probably use some optimization.
    //
    // In addition, this supports object serialization in a similar fashion.
    // We build an array of class types contained in this file, and write it
    // to RuntimeResourceReader header section of the file.  Every resource
    // will contain its type (as an index into the array of classes) with the data
    // for that resource.  We will use the Runtime's serialization support for this.
    //
    // All strings in the file format are written with BinaryReader and
    // BinaryWriter, which writes out the length of the String in bytes as an
    // Int32 then the contents as Unicode chars encoded in UTF-8.  In the name
    // table though, each resource name is written in UTF-16 so we can do a
    // string compare byte by byte against the contents of the file, without
    // allocating objects.  Ideally we'd have a way of comparing UTF-8 bytes
    // directly against a String object, but that may be a lot of work.
    //
    // The offsets of each resource string are relative to the beginning
    // of the Data section of the file.  This way, if a tool decided to add
    // one resource to a file, it would only need to increment the number of
    // resources, add the hash &amp; location of last byte in the name section
    // to the array of resource hashes and resource name positions (carefully
    // keeping these arrays sorted), add the name to the end of the name &amp;
    // offset list, possibly add the type list of types types (and increase
    // the number of items in the type table), and add the resource value at
    // the end of the file.  The other offsets wouldn't need to be updated to
    // reflect the longer header section.

    [Serializable]
    internal enum ResourceTypeCode
    {
        Null = 0,
        String = 1,
        Boolean = 2,
        Char = 3,
        Byte = 4,
        SByte = 5,
        Int16 = 6,
        UInt16 = 7,
        Int32 = 8,
        UInt32 = 9,
        Int64 = 10,
        UInt64 = 11,
        Single = 12,
        Double = 13,
        Decimal = 14,
        DateTime = 15,
        LastPrimitive = 16,
        TimeSpan = 16,
        ByteArray = 32,
        Stream = 33,
        StartOfUserTypes = 64,
    }

    internal class Res
    {
        public readonly String name;
        public readonly String type;
        public byte[] data;
        internal readonly int typeCode;
        internal readonly int dataPos;

        public Res(string name, string type, byte[] data, int typeCode, int dataPos)
        {
            this.name = name;
            this.type = type;
            this.data = data;
            this.typeCode = typeCode;
            this.dataPos = dataPos;
        }

        public bool IsBamlStream
        {
            get { return type == "ResourceTypeCode.Stream" && name != null && name.EndsWith(".baml"); }
        }

        public bool IsString
        {
            get { return type == "ResourceTypeCode.String" || type != null && type.StartsWith("System.String"); }
        }
    }

    internal sealed class ResReader : IEnumerable<Res>, IDisposable
    {
        private BinaryReader _store;    // backing store we're reading from.
        private readonly long _nameSectionOffset;  // Offset to name section of file.
        private readonly long _dataSectionOffset;  // Offset to Data section of file.
        private readonly int _numResources;    // Num of resources files, in case arrays aren't allocated.
        private readonly BinaryFormatter _bf;

        // Version number of .resources file, for compatibility
        private readonly int _version;

        private int[] _nameHashes;    // hash values for all names.
        private int[] _namePositions; // relative locations of names
        private int[] _typeNamePositions;  // To delay initialize type table

        public ResReader(Stream stream)
        {
            _store = new BinaryReader(stream, Encoding.UTF8);
            _bf = new BinaryFormatter(null, new StreamingContext(StreamingContextStates.File | StreamingContextStates.Persistence));
            try
            {
                // Read ResourceManager header
                // Check for magic number
                int magicNum = _store.ReadInt32();
                if (magicNum != ResourceManager.MagicNumber)
                    throw new ArgumentException("Resources_StreamNotValid");
                // Assuming this is ResourceManager header V1 or greater, hopefully
                // after the version number there is a number of bytes to skip
                // to bypass the rest of the ResMgr header.
                int resMgrHeaderVersion = _store.ReadInt32();
                if (resMgrHeaderVersion > 1)
                {
                    int numBytesToSkip = _store.ReadInt32();
                    _store.BaseStream.Seek(numBytesToSkip, SeekOrigin.Current);
                }
                else
                {
                    SkipInt32();    // We don't care about numBytesToSkip.

                    // Read in type name for a suitable ResourceReader
                    // Note ResourceWriter & InternalResGen use different Strings.
                    String readerType = _store.ReadString();

                    // Skip over type name for a suitable ResourceSet
                    SkipString();
                }

                // Read RuntimeResourceSet header
                // Do file version check
                int version = _store.ReadInt32();
                if (version != 2 && version != 1)
                    throw new ArgumentException("Arg_ResourceFileUnsupportedVersion");

                _version = version;

                _numResources = _store.ReadInt32();

                // Read type positions into type positions array.
                // But delay initialize the type table.
                int numTypes = _store.ReadInt32();
                _typeNamePositions = new int[numTypes];
                for (int i = 0; i < numTypes; i++)
                {
                    _typeNamePositions[i] = (int)_store.BaseStream.Position;

                    // Skip over the Strings in the file.  Don't create types.
                    SkipString();
                }

                // Prepare to read in the array of name hashes
                //  Note that the name hashes array is aligned to 8 bytes so
                //  we can use pointers into it on 64 bit machines. (4 bytes
                //  may be sufficient, but let's plan for the future)
                //  Skip over alignment stuff.  All public .resources files
                //  should be aligned   No need to verify the byte values.
                long pos = _store.BaseStream.Position;
                int alignBytes = ((int)pos) & 7;
                if (alignBytes != 0)
                {
                    for (int i = 0; i < 8 - alignBytes; i++)
                    {
                        _store.ReadByte();
                    }
                }

                // Read in the array of name hashes
                _nameHashes = new int[_numResources];
                for (int i = 0; i < _numResources; i++)
                    _nameHashes[i] = _store.ReadInt32();

                // Read in the array of relative positions for all the names.
                _namePositions = new int[_numResources];
                for (int i = 0; i < _numResources; i++)
                    _namePositions[i] = _store.ReadInt32();

                // Read location of data section.
                _dataSectionOffset = _store.ReadInt32();

                // Store current location as start of name section
                _nameSectionOffset = _store.BaseStream.Position;
            }
            catch (EndOfStreamException)
            {
                throw new BadImageFormatException("BadImageFormat_ResourcesHeaderCorrupted");
            }
            catch (IndexOutOfRangeException)
            {
                throw new BadImageFormatException("BadImageFormat_ResourcesHeaderCorrupted");
            }
        }

        public void Close()
        {
            Dispose(true);
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (_store != null)
            {
                if (disposing)
                    _store.Close();
                _store = null;
                _namePositions = null;
                _typeNamePositions = null;
                _nameHashes = null;
            }
        }

        private void SkipInt32()
        {
            _store.BaseStream.Seek(4, SeekOrigin.Current);
        }


        private void SkipString()
        {
            int stringLength = Read7BitEncodedInt();
            _store.BaseStream.Seek(stringLength, SeekOrigin.Current);
        }

        private int GetNamePosition(int index)
        {
            int r = _namePositions[index];
            if (r < 0 || r > _dataSectionOffset - _nameSectionOffset)
            {
                throw new FormatException("BadImageFormat_ResourcesNameOutOfSection");
            }
            return r;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<Res> GetEnumerator()
        {
            return GetResources();
        }

        public Object GetObject(Res res)
        {
            if (_version == 1)
                return GetObject_V1(res);
            return GetObject_V2(res);
        }

        private Object GetObject_V2(Res res)
        {
            lock (this)
            {
                _store.BaseStream.Seek(_dataSectionOffset + res.dataPos, SeekOrigin.Begin);
                ResourceTypeCode typeCode = (ResourceTypeCode)Read7BitEncodedInt();

                switch (typeCode)
                {
                    case ResourceTypeCode.Null:
                        return null;

                    case ResourceTypeCode.String:
                        return _store.ReadString();

                    case ResourceTypeCode.Boolean:
                        return _store.ReadBoolean();

                    case ResourceTypeCode.Char:
                        return (char)_store.ReadUInt16();

                    case ResourceTypeCode.Byte:
                        return _store.ReadByte();

                    case ResourceTypeCode.SByte:
                        return _store.ReadSByte();

                    case ResourceTypeCode.Int16:
                        return _store.ReadInt16();

                    case ResourceTypeCode.UInt16:
                        return _store.ReadUInt16();

                    case ResourceTypeCode.Int32:
                        return _store.ReadInt32();

                    case ResourceTypeCode.UInt32:
                        return _store.ReadUInt32();

                    case ResourceTypeCode.Int64:
                        return _store.ReadInt64();

                    case ResourceTypeCode.UInt64:
                        return _store.ReadUInt64();

                    case ResourceTypeCode.Single:
                        return _store.ReadSingle();

                    case ResourceTypeCode.Double:
                        return _store.ReadDouble();

                    case ResourceTypeCode.Decimal:
                        return _store.ReadDecimal();

                    case ResourceTypeCode.DateTime:
                        // Use DateTime's ToBinary & FromBinary.
                        Int64 data = _store.ReadInt64();
                        return DateTime.FromBinary(data);

                    case ResourceTypeCode.TimeSpan:
                        Int64 ticks = _store.ReadInt64();
                        return new TimeSpan(ticks);

                    // Special types
                    case ResourceTypeCode.ByteArray:
                        {
                            int len = _store.ReadInt32();
                            return _store.ReadBytes(len);
                        }
                    case ResourceTypeCode.Stream:
                        {
                            int len = _store.ReadInt32();
                            byte[] bytes = _store.ReadBytes(len);
                            // Lifetime of memory == lifetime of this stream.
                            return new MemoryStream(bytes);
                        }
                }

                // Normal serialized objects
                return _bf.Deserialize(_store.BaseStream);
            }
        }

        private Object GetObject_V1(Res res)
        {
            lock (this)
            {
                _store.BaseStream.Seek(_dataSectionOffset + res.dataPos, SeekOrigin.Begin);
                int typeIndex = Read7BitEncodedInt();
                if (typeIndex == -1)
                    return null;
                var typeName = TypeNameFromTypeIndex(typeIndex);
                var type = Type.GetType(typeName, true);
                if (type == typeof(string))
                    return this._store.ReadString();

                if (type == typeof(int))
                    return this._store.ReadInt32();

                if (type == typeof(byte))
                    return this._store.ReadByte();

                if (type == typeof(sbyte))
                    return this._store.ReadSByte();

                if (type == typeof(short))
                    return this._store.ReadInt16();

                if (type == typeof(long))
                    return this._store.ReadInt64();

                if (type == typeof(ushort))
                    return this._store.ReadUInt16();

                if (type == typeof(uint))
                    return this._store.ReadUInt32();

                if (type == typeof(ulong))
                    return this._store.ReadUInt64();

                if (type == typeof(float))
                    return this._store.ReadSingle();

                if (type == typeof(double))
                    return this._store.ReadDouble();

                if (type == typeof(DateTime))
                    return new DateTime(this._store.ReadInt64());

                if (type == typeof(TimeSpan))
                    return new TimeSpan(this._store.ReadInt64());

                if (type == typeof(decimal))
                {
                    int[] array = new int[4];
                    for (int i = 0; i < array.Length; i++)
                    {
                        array[i] = this._store.ReadInt32();
                    }
                    return new decimal(array);
                }

                // Normal serialized objects
                return _bf.Deserialize(_store.BaseStream);
            }
        }


        internal IEnumerator<Res> GetResources()
        {
            // Get the type information from the data section.  Also,
            // sort all of the data section's indexes to compute length of
            // the serialized data for this type (making sure to subtract
            // off the length of the type code).
            KeyValuePair<int, string>[] dataPositionsAndNames = new KeyValuePair<int, string>[_numResources];
            lock (this)
            {
                // Read all the positions of data within the data section.
                for (int i = 0; i < _numResources; i++)
                {
                    _store.BaseStream.Position = _nameSectionOffset + GetNamePosition(i);
                    // Skip over name of resource
                    int byteLen = Read7BitEncodedInt();
                    var bytes = _store.ReadBytes(byteLen);
                    if (bytes.Length != byteLen)
                        throw new FormatException("BadImageFormat_ResourceNameCorrupted_NameIndex");
                    dataPositionsAndNames[i] = new KeyValuePair<int, string>(_store.ReadInt32(), Encoding.Unicode.GetString(bytes, 0, byteLen));
                }
                Array.Sort(dataPositionsAndNames, (a,b) => a.Key-b.Key);

                for (int i = 0; i < _numResources; i++)
                {
                    int dataPos = dataPositionsAndNames[i].Key;
                    long nextData = (i < _numResources - 1) ? dataPositionsAndNames[i + 1].Key + _dataSectionOffset : _store.BaseStream.Length;

                    // Read type code then byte[]
                    _store.BaseStream.Position = _dataSectionOffset + dataPos;
                    int typeCode = Read7BitEncodedInt();
                    string resourceType = TypeNameFromTypeCode(typeCode);

                    int len = (int)(nextData - _store.BaseStream.Position);
                    byte[] bytes = _store.ReadBytes(len);
                    if (bytes.Length != len)
                        throw new FormatException("BadImageFormat_ResourceNameCorrupted");
                    yield return new Res(dataPositionsAndNames[i].Value, resourceType, bytes, typeCode, dataPos);
                }
            }
            yield break;
        }

        internal int Read7BitEncodedInt()
        {
            // Read out an Int32 7 bits at a time.  The high bit
            // of the byte when on means to continue reading more bytes.
            int count = 0;
            int shift = 0;
            byte b;
            do
            {
                // Check for a corrupted stream.  Read a max of 5 bytes.
                // In a future version, add a DataFormatException.
                if (shift == 5 * 7)  // 5 bytes max per Int32, shift += 7
                    throw new FormatException("Format_Bad7BitInt32");

                // ReadByte handles end of stream cases for us.
                b = _store.ReadByte();
                count |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return count;
        }

        private String TypeNameFromTypeIndex(int typeIndex)
        {
            long oldPos = _store.BaseStream.Position;
            try
            {
                _store.BaseStream.Position = _typeNamePositions[typeIndex];
                return _store.ReadString();
            }
            finally
            {
                _store.BaseStream.Position = oldPos;
            }
        }

        private String TypeNameFromTypeCode(int typeCode)
        {
            if (_version == 1)
            {
                return TypeNameFromTypeIndex(typeCode);
            }
            // _version == 2
            var tc = (ResourceTypeCode) typeCode;
            if (tc < ResourceTypeCode.StartOfUserTypes)
            {
                return "ResourceTypeCode." + tc;
            }
            else
            {
                return TypeNameFromTypeIndex(tc - ResourceTypeCode.StartOfUserTypes);
            }
        }

    }

}
