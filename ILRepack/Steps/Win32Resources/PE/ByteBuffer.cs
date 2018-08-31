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
using System;

namespace ILRepacking.Steps.Win32Resources.PE {

	class ByteBuffer {

		internal byte [] buffer;
		internal int length;
		internal int position;

		public ByteBuffer ()
		{
			this.buffer = new byte[0];
		}

		public ByteBuffer (int length)
		{
			this.buffer = new byte [length];
		}

		public void WriteUInt16 (ushort value)
		{
			if (position + 2 > buffer.Length)
				Grow (2);

			buffer [position++] = (byte) value;
			buffer [position++] = (byte) (value >> 8);

			if (position > length)
				length = position;
		}

		public void WriteInt16 (short value)
		{
			WriteUInt16 ((ushort) value);
		}

		public void WriteUInt32 (uint value)
		{
			if (position + 4 > buffer.Length)
				Grow (4);

			buffer [position++] = (byte) value;
			buffer [position++] = (byte) (value >> 8);
			buffer [position++] = (byte) (value >> 16);
			buffer [position++] = (byte) (value >> 24);

			if (position > length)
				length = position;
		}

		public void WriteInt32 (int value)
		{
			WriteUInt32 ((uint) value);
		}

		public void WriteBytes (byte [] bytes)
		{
			var length = bytes.Length;
			if (position + length > buffer.Length)
				Grow (length);

			Buffer.BlockCopy (bytes, 0, buffer, position, length);
			position += length;

			if (position > this.length)
				this.length = position;
		}

		public void WriteBytes (ByteBuffer buffer)
		{
			if (position + buffer.length > this.buffer.Length)
				Grow (buffer.length);

			Buffer.BlockCopy (buffer.buffer, 0, this.buffer, position, buffer.length);
			position += buffer.length;

			if (position > this.length)
				this.length = position;
		}

		void Grow (int desired)
		{
			var current = this.buffer;
			var current_length = current.Length;

			var buffer = new byte [Math.Max (current_length + desired, current_length * 2)];
			Buffer.BlockCopy (current, 0, buffer, 0, current_length);
			this.buffer = buffer;
		}

	    public void Align(int alignment)
	    {
	        if (position + alignment > buffer.Length)
	            Grow(alignment);
	        int newpos = (position + alignment - 1) & ~(alignment - 1);
	        while (position < newpos)
	            buffer[position++] = 0;
	    }
	}
}
