using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ILRepacking
{
    internal interface IFile
    {
        bool Exists(string path);

        string[] ReadAllLines(string path);
    }
}
