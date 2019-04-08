// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class StandardConsoleTests
    {
        [Fact]
        public static void TestStandardConsole()
        {
            XAssert.AreEqual(10, StandardConsole.GetFirstLineLength(new string('A', 10) + Environment.NewLine + new string('A', 10)));
            XAssert.AreEqual(1, StandardConsole.ComputeLinesUsedByString(new string('A', 10), 20));
            XAssert.AreEqual(2, StandardConsole.ComputeLinesUsedByString(new string('A', 10), 6));
            XAssert.AreEqual(1, StandardConsole.ComputeLinesUsedByString(new string('A', 10), 10));
        }
    }
}
