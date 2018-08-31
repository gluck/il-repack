//
// Copyright (c) 2018 Alexander Vostres
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using System;
using System.IO;
using System.Linq;

namespace ILRepacking.Steps.Win32Resources.PE
{
    class ImageReader : BinaryStreamReader
    {
        public bool Pe64 { get; private set; }

        public Section[] Sections { get; private set; }

        public ImageReader(Stream stream) : base(stream)
        {
            ReadImage();
        }

        void ReadImage()
        {
            if (BaseStream.Length < 128)
                throw new BadImageFormatException();

            // - DOSHeader

            // PE					2
            // Start				58
            // Lfanew				4
            // End					64

            if (ReadUInt16 () != 0x5a4d)
                throw new BadImageFormatException ();
            
            Advance (58);

            MoveTo (ReadUInt32 ());

            if (ReadUInt32 () != 0x00004550)
                throw new BadImageFormatException ();

            
            // - PEFileHeader

            // Machine				2
            
            Advance(2);

            // NumberOfSections		2
            var sectionCount = ReadUInt16();

            // TimeDateStamp		4
            // PointerToSymbolTable	4
            // NumberOfSymbols		4
            // OptionalHeaderSize	2
            // Characteristics		2
            Advance(16);
            
            ReadOptionalHeaders();
            
            Sections = ReadSections(sectionCount);
        }

        public SectionData GetSectionData(string name)
        {
            var src = GetSection(name);

            if (src == null)
            {
                return null;
            }

            MoveTo(src.PointerToRawData);
            var bytes = ReadBytes((int) src.SizeOfRawData);
            return new SectionData(src.VirtualAddress, bytes);
        }

        public Section GetSection(string name)
        {
            return Sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        }

        void ReadOptionalHeaders()
        {
            // - PEOptionalHeader
            //   - StandardFieldsHeader

            // Magic				2
            Pe64 = ReadUInt16() == 0x20b;

            //						pe32 || pe64
            // CodeSize				4
            // InitializedDataSize	4
            // UninitializedDataSize4
            // EntryPointRVA		4
            // BaseOfCode			4
            // BaseOfData			4 || 0

            //   - NTSpecificFieldsHeader

            // ImageBase			4 || 8
            // SectionAlignment		4
            // FileAlignement		4
            // OSMajor				2
            // OSMinor				2
            // UserMajor			2
            // UserMinor			2
            // SubSysMajor			2
            // SubSysMinor			2
            // Reserved				4
            // ImageSize			4
            // HeaderSize			4
            // FileChecksum			4
            // SubSystem			2
            // DLLFlags				2
            // StackReserveSize		4 || 8
            // StackCommitSize		4 || 8
            // HeapReserveSize		4 || 8
            // HeapCommitSize		4 || 8
            // LoaderFlags			4
            // NumberOfDataDir		4

            //   - DataDirectoriesHeader

            // ExportTable			8
            // ImportTable			8
            // ResourceTable		8
            // ExceptionTable		8
            // CertificateTable		8
            // BaseRelocationTable	8
            // Debug				8
            // Copyright			8
            // GlobalPtr			8
            // TLSTable				8
            // LoadConfigTable		8
            // BoundImport			8
            // IAT					8
            // DelayImportDescriptor8
            // CLIHeader			8
            // Reserved				8
            
            Advance(Pe64 ? 238 : 222);
        }

        string ReadZeroTerminatedString (int length)
        {
            int read = 0;
            var buffer = new char [length];
            var bytes = ReadBytes (length);
            while (read < length) {
                var current = bytes [read];
                if (current == 0)
                    break;

                buffer [read++] = (char) current;
            }

            return new string (buffer, 0, read);
        }

        Section[] ReadSections (ushort count)
        {
            var sections = new Section [count];

            for (int i = 0; i < count; i++) {
                var section = new Section ();

                // Name
                section.Name = ReadZeroTerminatedString (8);

                // VirtualSize		4
                section.VirtualSize = ReadUInt32 ();

                // VirtualAddress	4
                section.VirtualAddress = ReadUInt32 ();
                // SizeOfRawData	4
                section.SizeOfRawData = ReadUInt32 ();
                // PointerToRawData	4
                section.PointerToRawData = ReadUInt32 ();

                // PointerToRelocations		4
                // PointerToLineNumbers		4
                // NumberOfRelocations		2
                // NumberOfLineNumbers		2
                // Characteristics			4
                Advance (16);

                sections [i] = section;
            }

            return sections;
        }
    }
}
