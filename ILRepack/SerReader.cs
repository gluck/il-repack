/*
 * BinarySerializationStreamAnalysis - a simple demo class for parsing the
 *  output of the BinaryFormatter class' "Serialize" method, eg counting objects and
 *  values.
 *
 * Copyright Tao Klerks, 2010-2011, tao@klerks.biz
 * Licensed under the modified BSD license:
 *

Redistribution and use in source and binary forms, with or without modification, are
permitted provided that the following conditions are met:

 - Redistributions of source code must retain the above copyright notice, this list of
conditions and the following disclaimer.
 - Redistributions in binary form must reproduce the above copyright notice, this list
of conditions and the following disclaimer in the documentation and/or other materials
provided with the distribution.
 - The name of the author may not be used to endorse or promote products derived from
this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES,
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY
OF SUCH DAMAGE.

 *
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ILRepacking
{
    /*
     * Copied (and modified) from https://github.com/TaoK/BinarySerializationAnalysis
     *
     * Home-made Serialized data parser, that only parses records to map merged type references
     * Other than that, it just streams the data from the input to the output unchanged.
     */
    internal class SerReader
    {
        private readonly IRepackContext _repackContext;

        //yeah, I know, these could be better protected...
        internal readonly Dictionary<int, SerialObject> SerialObjectsFound = new Dictionary<int, SerialObject>();
        internal readonly Dictionary<int, BinaryLibrary> LibrariesFound = new Dictionary<int, BinaryLibrary>();

        //available to the other objects, used to read from the stream
        internal readonly BinaryReader reader;
        private readonly BinaryWriter writer;

        //marks the end of the serialization stream
        private bool endRecordReached;

        //used for returning an arbitrary number of nulls as defined by certain record types
        private int PendingNullCounter;

        private long pos;
        private long start, end;

        public SerReader(IRepackContext repackContext, Stream inputStream, Stream outputStream)
        {
            _repackContext = repackContext;
            reader = new BinaryReader(inputStream, Encoding.UTF8);
            pos = inputStream.Position;
            writer = new BinaryWriter(outputStream, Encoding.UTF8);
        }

        public void TransferMarked()
        {
            int length = (int)(start - pos);
            long prev = reader.BaseStream.Position;
            if (length > 0)
            {
                reader.BaseStream.Position = pos;
                writer.Write(reader.ReadBytes(length));
                pos = end;
                reader.BaseStream.Position = prev;
            }
        }

        public string ReadMarkString()
        {
            start = reader.BaseStream.Position;
            string str = reader.ReadString();
            end = reader.BaseStream.Position;
            return str;
        }

        public void FixTypeName(string assemblyName, string typeName)
        {
            string str2 = _repackContext.FixTypeName(assemblyName, typeName);
            if (typeName != str2)
            {
                TransferMarked();
                writer.Write(str2);
            }
        }

        public string ReadAssemblyName()
        {
            string str = ReadMarkString();
            string str2 = _repackContext.FixAssemblyName(str);
            if (str != str2)
            {
                TransferMarked();
                writer.Write(str2);
            }
            return str;
        }

        public string ReadAndFixString()
        {
            string str = ReadMarkString();
            string str2 = _repackContext.FixStr(str);
            if (str != str2)
            {
                TransferMarked();
                writer.Write(str2);
            }
            return str;
        }

        public void Stream()
        {
            //dig in
            while (!endRecordReached)
            {
                ParseRecord(null);
            }
            start = reader.BaseStream.Position;
            TransferMarked();
        }

        internal int? ParseRecord(SerialObject parentObject)
        {
            int? serialObjectReferenceID = null;
            if (PendingNullCounter == 0)
            {
                long startPosition = reader.BaseStream.Position;
                SerialObject si = null;
                RecordTypeEnumeration nextRecordType = (RecordTypeEnumeration)reader.ReadByte();
                switch (nextRecordType)
                {
                    case RecordTypeEnumeration.SerializedStreamHeader:
                        //header is 4 values that I wouldn't know what to do with (what type of message, what version, etc) - trash.
                        reader.ReadBytes(16);
                        break;
                    case RecordTypeEnumeration.ClassWithID:
                        //just two ints, read directly
                        si = new ClassInfo().ReadObjectId(this);
                        int refObj = reader.ReadInt32();
                        //Use the referenced object definition for data retrieval rules
                        // -> this will overwrite the original values in the referenced object, but who cares - the values are trash anyway (for now).
                        ((ClassInfo)SerialObjectsFound[refObj]).ReadValues(this);
                        break;
                    case RecordTypeEnumeration.SystemClassWithMembers:
                        si = new ClassInfo().ReadMembers(this).ReadValues(this);
                        break;
                    case RecordTypeEnumeration.ClassWithMembers:
                        si = new ClassInfo().ReadMembers(this).ReadLibraryId(this).ReadValues(this);
                        break;
                    case RecordTypeEnumeration.SystemClassWithMembersAndTypes:
                        si = new ClassInfo().ReadMembers(this).ReadTypeInfo(this).ReadValues(this);
                        break;
                    case RecordTypeEnumeration.ClassWithMembersAndTypes:
                        si = new ClassInfo().ReadMembers(this).ReadTypeInfo(this).ReadLibraryId(this).ReadValues(this);
                        break;
                    case RecordTypeEnumeration.BinaryObjectString:
                        si = new ObjectString().ReadObjectId(this).ReadString(this);
                        break;
                    case RecordTypeEnumeration.BinaryArray:
                        si = new BinaryArray().ReadStruct(this).ReadValues(this);
                        break;
                    case RecordTypeEnumeration.MemberPrimitiveTyped:
                        //Don't know how this can happen - I think it's for messages/remoting only
                        throw new NotImplementedException();
                    case RecordTypeEnumeration.MemberReference:
                        //just return the ID that was referenced.
                        serialObjectReferenceID = reader.ReadInt32();
                        break;
                    case RecordTypeEnumeration.ObjectNull:
                        //a single null; do nothing, as null is the default return value.
                        break;
                    case RecordTypeEnumeration.MessageEnd:
                        //do nothing, quit. Wasn't that fun?
                        endRecordReached = true;
                        break;
                    case RecordTypeEnumeration.BinaryLibrary:
                        int newLibraryID = reader.ReadInt32();
                        LibrariesFound.Add(newLibraryID, new BinaryLibrary { LibraryID = newLibraryID, Name = ReadAssemblyName() });
                        break;
                    case RecordTypeEnumeration.ObjectNullMultiple256:
                        //a sequence of nulls; return null, and start a counter to continue returning N nulls over the next calls.
                        PendingNullCounter = reader.ReadByte() - 1;
                        break;
                    case RecordTypeEnumeration.ObjectNullMultiple:
                        //a sequence of nulls; return null, and start a counter to continue returning N nulls over the next calls.
                        PendingNullCounter = reader.ReadInt32() - 1;
                        //not yet tested: if it happens, take a look around.
                        throw new NotImplementedException();
                    case RecordTypeEnumeration.ArraySinglePrimitive:
                        si = new BinaryArray(BinaryTypeEnumeration.Primitive).ReadObjectId(this).ReadLengths(this).ReadPrimitiveType(this).ReadValues(this);
                        break;
                    case RecordTypeEnumeration.ArraySingleObject:
                        si = new BinaryArray(BinaryTypeEnumeration.Object).ReadObjectId(this).ReadLengths(this).ReadValues(this);
                        //not yet tested: if it happens, take a look around.
                        throw new NotImplementedException();
                    case RecordTypeEnumeration.ArraySingleString:
                        si = new BinaryArray(BinaryTypeEnumeration.String).ReadObjectId(this).ReadLengths(this).ReadValues(this);
                        //not yet tested: if it happens, take a look around.
                        throw new NotImplementedException();
                    case RecordTypeEnumeration.MethodCall:
                        //messages/remoting functionality not implemented
                        throw new NotImplementedException();
                    case RecordTypeEnumeration.MethodReturn:
                        //messages/remoting functionality not implemented
                        throw new NotImplementedException();
                    default:
                        throw new Exception("Parsing appears to have failed dramatically. Unknown record type, we must be lost in the bytestream!");

                }

                //standard: if this was a serial object, add to list and record its length.
                if (si != null)
                {
                    if (parentObject != null)
                        si.ParentObjectID = parentObject.ObjectID;
                    SerialObjectsFound.Add(si.ObjectID, si);
                    return si.ObjectID;
                }
            }
            else
                PendingNullCounter--;
            return serialObjectReferenceID;
        }
    }

    internal class BinaryLibrary
    {
        public int LibraryID;
        public string Name;
    }

    internal interface SerialObject
    {
        int ObjectID { get; set; }
        long? ParentObjectID { get; set; }
    }

    internal interface TypeHoldingThing
    {
        SerialObject RelevantObject { get; set; }
        BinaryTypeEnumeration? BinaryType { get; set; }
        PrimitiveTypeEnumeration? PrimitiveType { get; set; }
        ClassTypeInfo TypeInfo { get; set; }
    }

    internal interface ValueHoldingThing
    {
        object Value { get; set; }
        object ValueRefID { get; set; }
    }

    internal class ObjectWithId : SerialObject
    {
        public int ObjectID { get; set; }
        public long? ParentObjectID { get; set; }

        public ObjectWithId ReadObjectId(SerReader analyzer)
        {
            ObjectID = analyzer.reader.ReadInt32();
            return this;
        }
    }

    internal class ClassInfo : ObjectWithId
    {
        internal ClassInfo() { }

        internal ClassInfo ReadMembers(SerReader analyzer)
        {
            ReadObjectId(analyzer);
            Name = analyzer.ReadMarkString();
            Members = new List<MemberInfo>(analyzer.reader.ReadInt32());
            for (int i = 0; i < Members.Capacity; i++)
            {
                Members.Add(new MemberInfo());
                Members[i].Name = analyzer.reader.ReadString();
                Members[i].RelevantObject = this;
            }
            return this;
        }

        internal ClassInfo ReadTypeInfo(SerReader analyzer)
        {
            //first get binary types
            foreach (MemberInfo member in Members)
            {
                member.BinaryType = (BinaryTypeEnumeration)analyzer.reader.ReadByte();
            }

            //then get additional infos where appropriate
            foreach (MemberInfo member in Members)
            {
                TypeHelper.GetTypeAdditionalInfo(member, analyzer);
            }
            return this;
        }

        public ClassInfo ReadValues(SerReader analyzer)
        {
            //then get additional infos where appropriate
            foreach (MemberInfo member in Members)
            {
                TypeHelper.GetTypeValue(member, member, analyzer);
            }
            return this;
        }

        public ClassInfo ReadLibraryId(SerReader analyzer)
        {
            int libraryId = analyzer.reader.ReadInt32();
            analyzer.FixTypeName(analyzer.LibrariesFound[libraryId].Name, Name);
            return this;
        }

        public new ClassInfo ReadObjectId(SerReader analyzer)
        {
            base.ReadObjectId(analyzer);
            return this;
        }

        public string Name;
        public List<MemberInfo> Members;
        public int ReferenceCount;
    }

    internal class MemberInfo : TypeHoldingThing, ValueHoldingThing
    {
        public string Name;
        public SerialObject RelevantObject { get; set; }
        public BinaryTypeEnumeration? BinaryType { get; set; }
        public PrimitiveTypeEnumeration? PrimitiveType { get; set; }
        public ClassTypeInfo TypeInfo { get; set; }
        public object Value { get; set; }
        public object ValueRefID { get; set; }
    }

    internal class ClassTypeInfo
    {
        public string TypeName;
        public int? LibraryID;
    }

    internal class ObjectString : ObjectWithId
    {
        public string String;

        public new ObjectString ReadObjectId(SerReader analyzer)
        {
            base.ReadObjectId(analyzer);
            return this;
        }

        public ObjectString ReadString(SerReader analyzer)
        {
            String = analyzer.ReadAndFixString();
            return this;
        }
    }

    internal class BinaryArray : ObjectWithId, TypeHoldingThing
    {
        internal BinaryArray() { }

        internal BinaryArray(BinaryTypeEnumeration type)
        {
            BinaryType = type;
            Rank = 1;
        }

        internal BinaryArray ReadStruct(SerReader analyzer)
        {
            ReadObjectId(analyzer);
            BinaryArrayTypeEnumeration arrayType = (BinaryArrayTypeEnumeration)analyzer.reader.ReadByte();
            Rank = analyzer.reader.ReadInt32();

            ReadLengths(analyzer);

            if (arrayType == BinaryArrayTypeEnumeration.SingleOffset ||
                    arrayType == BinaryArrayTypeEnumeration.JaggedOffset ||
                    arrayType == BinaryArrayTypeEnumeration.RectangularOffset)
            {
                LowerBounds = new List<int>(Rank);
                for (int i = 0; i < Rank; i++)
                    LowerBounds.Add(analyzer.reader.ReadInt32());
            }

            BinaryType = (BinaryTypeEnumeration)analyzer.reader.ReadByte();
            TypeHelper.GetTypeAdditionalInfo(this, analyzer);
            return this;
        }

        public BinaryArray ReadLengths(SerReader analyzer)
        {
            Lengths = new List<int>(Rank);
            for (int i = 0; i < Rank; i++)
            {
                Lengths.Add(analyzer.reader.ReadInt32());
            }
            return this;
        }

        public new BinaryArray ReadObjectId(SerReader analyzer)
        {
            base.ReadObjectId(analyzer);
            return this;
        }

        public BinaryArray ReadValues(SerReader analyzer)
        {
            MemberInfo junk = new MemberInfo();
            for (int i = 0; i < Slots; i++)
                TypeHelper.GetTypeValue(this, junk, analyzer);
            return this;
        }

        public SerialObject RelevantObject { get { return this; } set { throw new NotImplementedException(); } }
        public int Rank;
        public List<int> Lengths;
        public List<int> LowerBounds;

        public BinaryTypeEnumeration? BinaryType { get; set; }
        public PrimitiveTypeEnumeration? PrimitiveType { get; set; }
        public ClassTypeInfo TypeInfo { get; set; }

        private int Slots
        {
            get
            {
                int outValue = 1;
                foreach (int length in Lengths)
                    outValue = outValue * length;
                return outValue;
            }
        }

        public BinaryArray ReadPrimitiveType(SerReader analyzer)
        {
            PrimitiveType = (PrimitiveTypeEnumeration)analyzer.reader.ReadByte();
            return this;
        }
    }

    internal static class TypeHelper
    {
        internal static void GetTypeAdditionalInfo(TypeHoldingThing typeHolder, SerReader analyzer)
        {
            switch (typeHolder.BinaryType)
            {
                case BinaryTypeEnumeration.Primitive:
                    typeHolder.PrimitiveType = (PrimitiveTypeEnumeration)analyzer.reader.ReadByte();
                    break;
                case BinaryTypeEnumeration.String:
                    break;
                case BinaryTypeEnumeration.Object:
                    break;
                case BinaryTypeEnumeration.SystemClass:
                    typeHolder.TypeInfo = new ClassTypeInfo();
                    typeHolder.TypeInfo.TypeName = analyzer.ReadMarkString();
                    break;
                case BinaryTypeEnumeration.Class:
                    typeHolder.TypeInfo = new ClassTypeInfo();
                    typeHolder.TypeInfo.TypeName = analyzer.ReadMarkString();
                    int libraryId = analyzer.reader.ReadInt32();
                    analyzer.FixTypeName(analyzer.LibrariesFound[libraryId].Name, typeHolder.TypeInfo.TypeName);
                    break;
                case BinaryTypeEnumeration.ObjectArray:
                    break;
                case BinaryTypeEnumeration.StringArray:
                    break;
                case BinaryTypeEnumeration.PrimitiveArray:
                    typeHolder.PrimitiveType = (PrimitiveTypeEnumeration)analyzer.reader.ReadByte();
                    break;
            }
        }

        internal static void GetTypeValue(TypeHoldingThing typeHolder, ValueHoldingThing valueHolder, SerReader analyzer)
        {
            switch (typeHolder.BinaryType)
            {
                case BinaryTypeEnumeration.Primitive:
                    switch (typeHolder.PrimitiveType)
                    {
                        case PrimitiveTypeEnumeration.Boolean:
                            valueHolder.Value = analyzer.reader.ReadBoolean();
                            break;
                        case PrimitiveTypeEnumeration.Byte:
                            valueHolder.Value = analyzer.reader.ReadByte();
                            break;
                        case PrimitiveTypeEnumeration.Char:
                            valueHolder.Value = analyzer.reader.ReadChar();
                            break;
                        case PrimitiveTypeEnumeration.DateTime:
                            valueHolder.Value = DateTime.FromBinary(analyzer.reader.ReadInt64());
                            break;
                        case PrimitiveTypeEnumeration.Decimal:
                            string decimalValue = analyzer.reader.ReadString();
                            valueHolder.Value = decimal.Parse(decimalValue);
                            break;
                        case PrimitiveTypeEnumeration.Double:
                            valueHolder.Value = analyzer.reader.ReadDouble();
                            break;
                        case PrimitiveTypeEnumeration.Int16:
                            valueHolder.Value = analyzer.reader.ReadInt16();
                            break;
                        case PrimitiveTypeEnumeration.Int32:
                            valueHolder.Value = analyzer.reader.ReadInt32();
                            break;
                        case PrimitiveTypeEnumeration.Int64:
                            valueHolder.Value = analyzer.reader.ReadInt64();
                            break;
                        case PrimitiveTypeEnumeration.Null:
                            valueHolder.Value = null;
                            break;
                        case PrimitiveTypeEnumeration.SByte:
                            valueHolder.Value = analyzer.reader.ReadSByte();
                            break;
                        case PrimitiveTypeEnumeration.Single:
                            valueHolder.Value = analyzer.reader.ReadSingle();
                            break;
                        case PrimitiveTypeEnumeration.String:
                            valueHolder.Value = analyzer.ReadAndFixString();
                            break;
                        case PrimitiveTypeEnumeration.TimeSpan:
                            valueHolder.Value = TimeSpan.FromTicks(analyzer.reader.ReadInt64());
                            break;
                        case PrimitiveTypeEnumeration.UInt16:
                            valueHolder.Value = analyzer.reader.ReadUInt16();
                            break;
                        case PrimitiveTypeEnumeration.UInt32:
                            valueHolder.Value = analyzer.reader.ReadUInt32();
                            break;
                        case PrimitiveTypeEnumeration.UInt64:
                            valueHolder.Value = analyzer.reader.ReadUInt64();
                            break;
                    }
                    break;
                case BinaryTypeEnumeration.String:
                    valueHolder.ValueRefID = analyzer.ParseRecord(typeHolder.RelevantObject);
                    break;
                case BinaryTypeEnumeration.Object:
                    valueHolder.ValueRefID = analyzer.ParseRecord(typeHolder.RelevantObject);
                    break;
                case BinaryTypeEnumeration.SystemClass:
                    valueHolder.ValueRefID = analyzer.ParseRecord(typeHolder.RelevantObject);
                    break;
                case BinaryTypeEnumeration.Class:
                    valueHolder.ValueRefID = analyzer.ParseRecord(typeHolder.RelevantObject);
                    break;
                case BinaryTypeEnumeration.ObjectArray:
                    valueHolder.ValueRefID = analyzer.ParseRecord(typeHolder.RelevantObject);
                    break;
                case BinaryTypeEnumeration.StringArray:
                    valueHolder.ValueRefID = analyzer.ParseRecord(typeHolder.RelevantObject);
                    break;
                case BinaryTypeEnumeration.PrimitiveArray:
                    valueHolder.ValueRefID = analyzer.ParseRecord(typeHolder.RelevantObject);
                    break;
            }
        }
    }


    internal enum RecordTypeEnumeration
    {
        SerializedStreamHeader = 0,
        ClassWithID = 1,                    //Object,
        SystemClassWithMembers = 2,         //ObjectWithMap,
        ClassWithMembers = 3,               //ObjectWithMapAssemId,
        SystemClassWithMembersAndTypes = 4, //ObjectWithMapTyped,
        ClassWithMembersAndTypes = 5,       //ObjectWithMapTypedAssemId,
        BinaryObjectString = 6,             //ObjectString,
        BinaryArray = 7,                    //Array,
        MemberPrimitiveTyped = 8,
        MemberReference = 9,
        ObjectNull = 10,
        MessageEnd = 11,
        BinaryLibrary = 12,                 //Assembly,
        ObjectNullMultiple256 = 13,
        ObjectNullMultiple = 14,
        ArraySinglePrimitive = 15,
        ArraySingleObject = 16,
        ArraySingleString = 17,
        //CrossAppDomainMap,
        //CrossAppDomainString,
        //CrossAppDomainAssembly,
        MethodCall = 21,
        MethodReturn = 22
    }

    internal enum BinaryTypeEnumeration
    {
        Primitive = 0,
        String = 1,
        Object = 2,
        SystemClass = 3,
        Class = 4,
        ObjectArray = 5,
        StringArray = 6,
        PrimitiveArray = 7
    }

    internal enum PrimitiveTypeEnumeration
    {
        Boolean = 1,
        Byte = 2,
        Char = 3,
        //unused
        Decimal = 5,
        Double = 6,
        Int16 = 7,
        Int32 = 8,
        Int64 = 9,
        SByte = 10,
        Single = 11,
        TimeSpan = 12,
        DateTime = 13,
        UInt16 = 14,
        UInt32 = 15,
        UInt64 = 16,
        Null = 17,
        String = 18
    }

    internal enum BinaryArrayTypeEnumeration
    {
        Single = 0,
        Jagged = 1,
        Rectangular = 2,
        SingleOffset = 3,
        JaggedOffset = 4,
        RectangularOffset = 5
    }
}
