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
using System;
using System.Collections.Generic;

namespace ILRepacking.Steps.Win32Resources
{
    class ResourceDirectory
    {
        private readonly List<ResourceEntry> _entries = new List<ResourceEntry>();

        public List<ResourceEntry> Entries
        {
            get { return _entries; }
        }

        public ushort SortEntries()
        {
            _entries.Sort(EntryComparer.INSTANCE);
            for (ushort i = 0; i < _entries.Count; i++)
                if (_entries[i].Name == null)
                    return i;
            return 0;
        }

        public ushort NumNameEntries { get; set; }
        public ushort NumIdEntries { get; set; }
        public ushort MinVersion { get; set; }
        public ushort MajorVersion { get; set; }
        public uint Characteristics { get; set; }
        public uint TimeDateStamp { get; set; }
    }

    class EntryComparer : IComparer<ResourceEntry>
    {
        internal static readonly EntryComparer INSTANCE = new EntryComparer();

        public int Compare(ResourceEntry x, ResourceEntry y)
        {
            if (x.Name != null && y.Name == null)
                return -1;
            if (x.Name == null && y.Name != null)
                return 1;
            if (x.Name == null)
                return (int) (x.Id - y.Id);
            return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}

