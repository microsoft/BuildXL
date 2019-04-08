// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class ByteSizeFormatterTests : XunitBuildXLTest
    {
        public ByteSizeFormatterTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void FormatByteSize()
        {
            XAssert.AreEqual(
                "1 B",
                ByteSizeFormatter.Format(1));

            XAssert.AreEqual(
                "1.00 KB",
                ByteSizeFormatter.Format(1024));

            XAssert.AreEqual(
                "1.50 MB",
                ByteSizeFormatter.Format((1024 * 1024) + (512 * 1024) + 1));

            XAssert.AreEqual(
                "999.00 GB",
                ByteSizeFormatter.Format(999L * 1024 * 1024 * 1024));
        }
    }
}
