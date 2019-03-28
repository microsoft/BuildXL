// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "PreserveOutputsTests")]
    public class PreserveOutputsReuseOutputsTests : PreserveOutputsTests
    {
        public PreserveOutputsReuseOutputsTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.ReuseOutputsOnDisk = true;
        }
    }
}
