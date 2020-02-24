//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
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
using System.IO;

namespace ILRepacking.Steps.Win32Resources.PE {

	class BinaryStreamWriter : BinaryWriter {

		public int Position {
			get { return (int) BaseStream.Position; }
			set { BaseStream.Position = value; }
		}

		public BinaryStreamWriter (Stream stream)
			: base (stream)
		{
		}

		public void WriteUInt16 (ushort value)
		{
			Write (value);
		}

		public void WriteUInt32 (uint value)
		{
			Write (value);
		}

		public void WriteBytes (byte [] bytes)
		{
			Write (bytes);
		}
	}
}