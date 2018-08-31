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
using RVA = System.UInt32;

namespace ILRepacking.Steps.Win32Resources.PE
{
    class ImageWriter : BinaryStreamWriter
    {
        const uint pe_header_size = 0x98u;
        const uint section_header_size = 0x28u;
        const uint file_alignment = 0x200;
        const uint section_alignment = 0x2000;

        const RVA text_rva = 0x2000;

        private readonly ImageReader _source;
        private ByteBuffer win32_resources;
        private Section _origText;
        private Section _text;
        private Section _rsrc;
        private Section _origReloc;
        private Section _reloc;
        private bool _createdRsrc;
        private ushort _sections;

        public ImageWriter(ImageReader source, Stream stream) : base(stream)
        {
            _source = source;
        }

        public void Prepare(ResourceDirectory merged)
        {
            _origText = _source.GetSection(".text");
            if (_origText == null)
            {
                throw new BadImageFormatException();
            }

            _text = CloneSectionHeader(_origText);
            RelocateSection(_text, null);

            _rsrc = _source.GetSection(".rsrc");
            _origReloc = _source.GetSection(".reloc");

            _sections = (ushort) (_origReloc == null ? 2 : 3);

            if (_rsrc == null)
            {
                _rsrc = CreateSection(".rsrc", _text);
            }

            win32_resources = RsrcWriter.WriteWin32ResourcesDirectory(_rsrc.VirtualAddress, merged);
            SetSectionSize(_rsrc, (uint) win32_resources.length);

            if (_origReloc != null)
            {
                _reloc = CloneSectionHeader(_origReloc);
                RelocateSection(_reloc, _rsrc);
            }
        }

        private void RelocateSection(Section ret, Section previous)
        {
            ret.VirtualAddress = previous != null
                ? previous.VirtualAddress + Align(previous.VirtualSize, section_alignment)
                : text_rva;
            ret.PointerToRawData = previous != null
                ? previous.PointerToRawData + previous.SizeOfRawData
                : Align(GetHeaderSize(), file_alignment);
        }

        void SetSectionSize(Section ret, uint size)
        {
            ret.VirtualSize = size;
            ret.SizeOfRawData = Align(size, file_alignment);
        }

        Section CreateSection(string name, Section previous)
        {
            return new Section
            {
                Name = name,
                VirtualAddress = previous.VirtualAddress + Align (previous.VirtualSize, section_alignment),
                PointerToRawData = previous.PointerToRawData + previous.SizeOfRawData
            };
        }

        Section CloneSectionHeader(Section src)
        {
            return new Section
            {
                Name = src.Name, PointerToRawData = src.PointerToRawData, SizeOfRawData = src.SizeOfRawData,
                VirtualAddress = src.VirtualAddress, VirtualSize = src.VirtualSize
            };
        }

        static uint Align (uint value, uint align)
        {
            align--;
            return (value + align) & ~align;
        }

        public void Write()
        {
            _source.Position = 0;

            CopyBytes(60);
            var offset = _source.ReadUInt32();
            WriteUInt32(offset);
            if (offset > Position)
            {
                CopyBytes((int) (offset - Position));
            }
            CopyBytes(6);

            var sectionCount = _reloc == null ? 2 : 3;

            WriteUInt16((ushort) sectionCount);
            CopyBytes(16);

            WriteOptionalHeaders();
            WriteSectionHeaders();
            CopySection(_origText, _text);
            WriteSection(_rsrc, win32_resources);
            CopySection(_origReloc, _reloc);
        }

        private void CopyBytes(int bytes)
        {
            _source.Position = Position;
            WriteBytes(_source.ReadBytes(bytes));
        }

        private void WriteSection(Section section, ByteBuffer data)
        {
            MoveTo(section.PointerToRawData);

            Write(data.buffer, 0, data.length);

            if (data.length < section.SizeOfRawData)
            {
                var paddingCount = section.SizeOfRawData - data.length;
                var padding = new byte[paddingCount];
                WriteBytes(padding);
            }
        }

        void MoveTo (uint pointer)
        {
            BaseStream.Seek (pointer, SeekOrigin.Begin);
        }

        private void CopySection(Section from, Section to)
        {
            const int buffer_size = 4096;

            if (from.SizeOfRawData != to.SizeOfRawData)
            {
                throw new InvalidOperationException();
            }

            _source.MoveTo(from.PointerToRawData);
            MoveTo(to.PointerToRawData);

            if (from.SizeOfRawData <= buffer_size) {
                Write (_source.ReadBytes(buffer_size));
                return;
            }

            var written = 0;
            var buffer = new byte [buffer_size];
            while (written != from.SizeOfRawData) {
                var writeSize = Math.Min((int) from.SizeOfRawData - written, buffer_size);
                _source.Read(buffer, 0, writeSize);
                Write (buffer, 0, writeSize);
                written += writeSize;
            }
        }

        private void WriteOptionalHeaders()
        {
            var pe64 = _source.Pe64;

            CopyBytes(8);
            WriteUInt32 ((_reloc?.SizeOfRawData ?? 0)
                         + (_rsrc?.SizeOfRawData ?? 0));	// InitializedDataSize

            CopyBytes(44);

            var last_section = _reloc ?? _rsrc ?? _text;
            WriteUInt32 (last_section.VirtualAddress + Align (last_section.VirtualSize, section_alignment));	// ImageSize
            WriteUInt32 (_text.PointerToRawData);	// HeaderSize

            CopyBytes(pe64 ? 64 : 48);

            WriteUInt32 (_rsrc?.VirtualAddress ?? 0);		    // ResourceTable
            WriteUInt32 (_rsrc?.VirtualSize ?? 0);

            CopyBytes(16);

            WriteUInt32 (_reloc?.VirtualAddress ?? 0);			// BaseRelocationTable
            WriteUInt32 (_reloc?.VirtualSize ?? 0);
            
            CopyBytes(80);
        }
        
        void WriteSectionHeaders ()
        {
            WriteSectionHeader (_origText, 0x60000020);

            if (_rsrc != null)
                WriteSectionHeader (_rsrc, 0x40000040);

            if (_reloc != null)
                WriteSectionHeader (_reloc, 0x42000040);
        }

        void WriteSectionHeader (Section section, uint characteristics)
        {
            var name = new byte [8];
            var sect_name = section.Name;
            for (int i = 0; i < sect_name.Length; i++)
                name [i] = (byte) sect_name [i];

            WriteBytes (name);
            WriteUInt32 (section.VirtualSize);
            WriteUInt32 (section.VirtualAddress);
            WriteUInt32 (section.SizeOfRawData);
            WriteUInt32 (section.PointerToRawData);
            WriteUInt32 (0);	// PointerToRelocations
            WriteUInt32 (0);	// PointerToLineNumbers
            WriteUInt16 (0);	// NumberOfRelocations
            WriteUInt16 (0);	// NumberOfLineNumbers
            WriteUInt32 (characteristics);
        }
        
        static ushort SizeOfOptionalHeader (bool pe64)
        {
            return (ushort) (!pe64 ? 0xe0 : 0xf0);
        }

        public uint GetHeaderSize ()
        {
            return pe_header_size + SizeOfOptionalHeader (_source.Pe64) + (_sections * section_header_size);
        }
    }
}
