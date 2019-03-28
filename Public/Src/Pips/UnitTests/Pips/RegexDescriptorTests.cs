// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Pips
{
    public sealed class RegexDescriptorTests : XunitBuildXLTest
    {
        public RegexDescriptorTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void RegexDescriptorEquality()
        {
            var stringTable = new StringTable(0);
            StructTester.TestEquality(
                baseValue: new RegexDescriptor(StringId.Create(stringTable, ".*"), RegexOptions.IgnoreCase),
                equalValue: new RegexDescriptor(StringId.Create(stringTable, ".*"), RegexOptions.IgnoreCase),
                notEqualValues: new[]
                                {
                                    new RegexDescriptor(StringId.Create(stringTable, "[a-z]"), RegexOptions.IgnoreCase),
                                    new RegexDescriptor(StringId.Create(stringTable, ".*"), RegexOptions.ExplicitCapture),
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right);
        }
    }
}
