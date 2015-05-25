using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ILRepacking
{
    // copied from IKVM source
    internal sealed class LineNumberWriter
    {
        private System.IO.MemoryStream stream;
        private int prevILOffset;
        private int prevLineNum;
        private int count;

        public LineNumberWriter(int estimatedCount)
        {
            stream = new System.IO.MemoryStream(estimatedCount * 2);
        }

        public void AddMapping(int ilOffset, int linenumber)
        {
            if (count == 0)
            {
                if (ilOffset == 0 && linenumber != 0)
                {
                    prevLineNum = linenumber;
                    count++;
                    WritePackedInteger(linenumber - (64 + 50));
                    return;
                }
                else
                {
                    prevLineNum = linenumber & ~3;
                    WritePackedInteger(((-prevLineNum / 4) - (64 + 50)));
                }
            }
            bool pc_overflow;
            bool lineno_overflow;
            byte lead;
            int deltaPC = ilOffset - prevILOffset;
            if (deltaPC >= 0 && deltaPC < 31)
            {
                lead = (byte)deltaPC;
                pc_overflow = false;
            }
            else
            {
                lead = (byte)31;
                pc_overflow = true;
            }
            int deltaLineNo = linenumber - prevLineNum;
            const int bias = 2;
            if (deltaLineNo >= -bias && deltaLineNo < 7 - bias)
            {
                lead |= (byte)((deltaLineNo + bias) << 5);
                lineno_overflow = false;
            }
            else
            {
                lead |= (byte)(7 << 5);
                lineno_overflow = true;
            }
            stream.WriteByte(lead);
            if (pc_overflow)
            {
                WritePackedInteger(deltaPC - (64 + 31));
            }
            if (lineno_overflow)
            {
                WritePackedInteger(deltaLineNo);
            }
            prevILOffset = ilOffset;
            prevLineNum = linenumber;
            count++;
        }

        public int Count
        {
            get
            {
                return count;
            }
        }

        public int LineNo
        {
            get
            {
                return prevLineNum;
            }
        }

        public byte[] ToArray()
        {
            return stream.ToArray();
        }

        /*
         * packed integer format:
         * ----------------------
         * 
         * First byte:
         * 00 - 7F      Single byte integer (-64 - 63)
         * 80 - BF      Double byte integer (-8192 - 8191)
         * C0 - DF      Triple byte integer (-1048576 - 1048576)
         * E0 - FE      Reserved
         * FF           Five byte integer
         */
        private void WritePackedInteger(int val)
        {
            if (val >= -64 && val < 64)
            {
                val += 64;
                stream.WriteByte((byte)val);
            }
            else if (val >= -8192 && val < 8192)
            {
                val += 8192;
                stream.WriteByte((byte)(0x80 + (val >> 8)));
                stream.WriteByte((byte)val);
            }
            else if (val >= -1048576 && val < 1048576)
            {
                val += 1048576;
                stream.WriteByte((byte)(0xC0 + (val >> 16)));
                stream.WriteByte((byte)(val >> 8));
                stream.WriteByte((byte)val);
            }
            else
            {
                stream.WriteByte(0xFF);
                stream.WriteByte((byte)(val >> 24));
                stream.WriteByte((byte)(val >> 16));
                stream.WriteByte((byte)(val >> 8));
                stream.WriteByte((byte)(val >> 0));
            }
        }
    }
}
