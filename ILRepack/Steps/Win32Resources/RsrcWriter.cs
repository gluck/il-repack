//
// Copyright (c) 2012 Francois Valdy
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
using System.Collections.Generic;
using ILRepacking.Steps.Win32Resources.PE;

namespace ILRepacking.Steps.Win32Resources
{
    class RsrcWriter
    {
        private static int GetDirectoryLength(ResourceDirectory dir)
		{
			int length = 16 + dir.Entries.Count * 8;
			foreach (ResourceEntry entry in dir.Entries)
				length += GetDirectoryLength(entry);
			return length;
		}
		private static int GetDirectoryLength(ResourceEntry entry)
		{
			if (entry.Data != null)
				return 16;
			return GetDirectoryLength(entry.Directory);
		}

		public static ByteBuffer WriteWin32ResourcesDirectory(uint virtualAddress, ResourceDirectory directory)
		{
			var result = new ByteBuffer();
			if (directory.Entries.Count != 0)
			{
				int stringTableOffset = GetDirectoryLength(directory);
				Dictionary<string, int> strings = new Dictionary<string, int>();
				ByteBuffer stringTable = new ByteBuffer(16);
				int offset = 16 + directory.Entries.Count * 8;
				for (int pass = 0; pass < 3; pass++)
					Write(result, directory, pass, 0, ref offset, strings, ref stringTableOffset, stringTable);
				// the pecoff spec says that the string table is between the directory entries and the data entries,
				// but the windows linker puts them after the data entries, so we do too.
				stringTable.Align(4);
				offset += stringTable.length;
				WriteResourceDataEntries(result, virtualAddress, directory, ref offset);
			    result.WriteBytes(stringTable);
				WriteData(result, directory);
			}

		    return result;
		}
		private static void WriteResourceDataEntries(ByteBuffer win32_resources, uint virtualAddress, ResourceDirectory directory, ref int offset)
		{
			foreach (ResourceEntry entry in directory.Entries)
			{
				if (entry.Data != null)
				{
					win32_resources.WriteUInt32((uint) (virtualAddress + offset));
					win32_resources.WriteInt32(entry.Data.Length);
					win32_resources.WriteUInt32(entry.CodePage);
					win32_resources.WriteUInt32(entry.Reserved);
					offset += (entry.Data.Length + 3) & ~3;
				}
				else
				{
					WriteResourceDataEntries(win32_resources, virtualAddress, entry.Directory, ref offset);
				}
			}
		}
		private static void WriteData(ByteBuffer win32_resources, ResourceDirectory directory)
		{
			foreach (ResourceEntry entry in directory.Entries)
			{
				if (entry.Data != null)
				{
					win32_resources.WriteBytes(entry.Data);
					win32_resources.Align(4);
				}
				else
				{
					WriteData(win32_resources, entry.Directory);
				}
			}
		}
		private static void Write(ByteBuffer win32_resources, ResourceDirectory directory, int writeDepth, int currentDepth, ref int offset, Dictionary<string, int> strings, ref int stringTableOffset, ByteBuffer stringTable)
		{
			if (currentDepth == writeDepth)
			{
				ushort namedEntries = directory.SortEntries();
				// directory header
				win32_resources.WriteUInt32(directory.Characteristics);
				win32_resources.WriteUInt32(directory.TimeDateStamp);
				win32_resources.WriteUInt16(directory.MajorVersion);
				win32_resources.WriteUInt16(directory.MinVersion);
				win32_resources.WriteUInt16(namedEntries);
				win32_resources.WriteUInt16((ushort)(directory.Entries.Count - namedEntries));
				foreach (ResourceEntry entry in directory.Entries)
				{
					WriteEntry(win32_resources, entry, ref offset, strings, ref stringTableOffset, stringTable);
				}
			}
			else
			{
				foreach (ResourceEntry entry in directory.Entries)
				{
					Write(win32_resources, entry.Directory, writeDepth, currentDepth + 1, ref offset, strings, ref stringTableOffset, stringTable);
				}
			}
		}
		private static void WriteEntry(ByteBuffer win32_resources, ResourceEntry entry, ref int offset, Dictionary<string, int> strings, ref int stringTableOffset, ByteBuffer stringTable)
		{
			WriteNameOrOrdinal(win32_resources, entry, strings, ref stringTableOffset, stringTable);
			if (entry.Data == null)
			{
				win32_resources.WriteUInt32(0x80000000U | (uint)offset);
				offset += entry.Directory.Entries.Count * 8;
			}
			else
			{
				win32_resources.WriteUInt32((uint)offset);
			}
			offset += 16;
		}
		private static void WriteNameOrOrdinal(ByteBuffer win32_resources, ResourceEntry entry, Dictionary<string, int> strings, ref int stringTableOffset, ByteBuffer stringTable)
		{
			if (entry.Name == null)
			{
				win32_resources.WriteUInt32(entry.Id);
			}
			else
			{
				int stringOffset;
				if (!strings.TryGetValue(entry.Name, out stringOffset))
				{
					stringOffset = stringTableOffset;
					strings.Add(entry.Name, stringOffset);
					stringTableOffset += entry.Name.Length * 2 + 2;
					stringTable.WriteUInt16((ushort)entry.Name.Length);
					foreach (char c in entry.Name)
						stringTable.WriteInt16((short)c);
				}
				win32_resources.WriteUInt32(0x80000000U | (uint)stringOffset);
			}
		}
    }
}
