// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Explorer.Server.Models
{
    public class BuildSummary : BuildRef
    {
        public DateTime StartTime { get; set; }

        public TimeSpan Duration { get; set; }

        public BuildState State { get; set; }

        public PipStats PipStats { get; set; }

        public PerfProfile OverAllBreakDown { get; set; }
    }
}
