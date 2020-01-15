// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.Explorer.Server.Models
{
    public class PerfProfile
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public TimeSpan Duration { get; set; }

        public IReadOnlyList<PerfProfile> NestedSteps { get; set; }
    }
}
