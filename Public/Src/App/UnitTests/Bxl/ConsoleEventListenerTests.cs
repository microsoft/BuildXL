// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL
{
    public class ConsoleEventListenerTests
    {
        [Fact]
        public void TestPercentDone()
        {
            XAssert.AreEqual("100.00%  ", ConsoleEventListener.ComputePercentDone(23, 23, 0, 0));
            XAssert.AreEqual("100.00%  ", ConsoleEventListener.ComputePercentDone(23, 23, 43, 43));
            XAssert.AreEqual("99.99%  ", ConsoleEventListener.ComputePercentDone(99998, 99999, 43, 43));
            XAssert.AreEqual("99.99%  ", ConsoleEventListener.ComputePercentDone(23, 23, 99998, 99999));
            XAssert.AreEqual("99.95%  ", ConsoleEventListener.ComputePercentDone(1000, 1000, 50, 100));
            XAssert.AreEqual("99.95%  ", ConsoleEventListener.ComputePercentDone(9999, 10000, 50, 100));
        }
    }
}
