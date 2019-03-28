// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !DISABLE_FEATURE_BOND_RPC

using System;
using BuildXL.Engine.Distribution;
using BuildXL.Engine.Distribution.InternalBond;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine
{
    public sealed class BondCallStateTests : XunitBuildXLTest
    {
        public BondCallStateTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void BondCallStateAsString()
        {
            foreach (BondCallState val in Enum.GetValues(typeof(BondCallState)))
            {
                XAssert.IsFalse(string.IsNullOrEmpty(val.AsString()));
            }
        }
    }
}

#endif