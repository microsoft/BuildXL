// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
