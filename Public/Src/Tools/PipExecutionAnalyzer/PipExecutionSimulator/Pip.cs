// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PipExecutionSimulator
{
    public class Pip
    {
        public uint Id { get; set; }
        public uint ExecutionTime { get; set; }
    }

    public class HashSourceFile : Pip
    {
        public string File { get; set; }
    }

    public class UserPip : Pip
    {
        public long StableId { get; set; }
        public Provenance Provenance { get; set; }
    }

    public class Provenance
    {
        public string FilePath { get; set; }
        public string ValueName { get; set; }
    }

}
