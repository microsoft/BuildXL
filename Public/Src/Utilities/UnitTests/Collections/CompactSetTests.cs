// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class CompactSetTests : XunitBuildXLTest
    {
        public CompactSetTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void Mix()
        {
            var r = new Random(0);
            for (int i = 0; i < 100; i++)
            {
                var s = default(CompactSet<int>);
                var t = new HashSet<int>();

                for (int j = 0; j < 100; j++)
                {
                    int value = r.Next(10);
                    switch (r.Next(3))
                    {
                        case 0:
                        case 1:
                            s = s.Add(value);
                            t.Add(value);
                            break;
                        case 2:
                            s = s.Remove(value);
                            t.Remove(value);
                            break;
                    }

                    XAssert.AreEqual(s.Count, t.Count);
                    foreach (int x in s)
                    {
                        XAssert.IsTrue(t.Contains(x));
                    }

                    foreach (int x in t)
                    {
                        XAssert.IsTrue(s.Contains(x));
                    }
                }
            }
        }
    }
}
