// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.Tracing;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class EventMaskTests
    {
        [Fact]
        public void TestEnabledEvents()
        {
            EventMask mask = new EventMask(enabledEvents: new int[] { 1, 27, 5 }, disabledEvents: null, nonMaskableLevel: EventLevel.Warning);

            // Test event id based masking
            XAssert.IsFalse(mask.IsEnabled(EventLevel.Informational, 10));
            XAssert.IsTrue(mask.IsEnabled(EventLevel.Informational, 27));

            // Test level based masking
            XAssert.IsTrue(mask.IsEnabled(EventLevel.Error, 10));
            XAssert.IsTrue(mask.IsEnabled(EventLevel.Warning, 10));
            XAssert.IsFalse(mask.IsEnabled(EventLevel.Verbose, 10));
        }

        [Fact]
        public void TestDisabledEvents()
        {
            EventMask mask = new EventMask(enabledEvents: null, disabledEvents: new int[] { 1, 27, 5 }, nonMaskableLevel: EventLevel.Warning);

            XAssert.IsTrue(mask.IsEnabled(EventLevel.Informational, 10));
            XAssert.IsFalse(mask.IsEnabled(EventLevel.Informational, 27));

            XAssert.IsTrue(mask.IsEnabled(EventLevel.Error, 1));
            XAssert.IsTrue(mask.IsEnabled(EventLevel.Warning, 1));
            XAssert.IsFalse(mask.IsEnabled(EventLevel.Verbose, 1));
        }
    }
}
