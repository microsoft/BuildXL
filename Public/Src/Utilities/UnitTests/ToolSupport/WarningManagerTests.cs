// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.ToolSupport;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.ToolSupport
{
    public sealed class WarningManagerTests : XunitBuildXLTest
    {
        public WarningManagerTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void WarningManager()
        {
            var wm = new WarningManager();

            // ensure the defaults are cool...
            Assert.False(wm.AllWarningsAreErrors);
            for (int i = 0; i < 10000; i++)
            {
                Assert.Equal(WarningState.AsWarning, wm.GetState(i));
            }

            // make sure setting warning as error has the desired effect
            wm.AllWarningsAreErrors = true;
            for (int i = 0; i < 10000; i++)
            {
                Assert.Equal(WarningState.AsError, wm.GetState(i));
            }

            // make sure unsetting warning as error has the desired effect
            wm.AllWarningsAreErrors = false;
            for (int i = 0; i < 10000; i++)
            {
                Assert.Equal(WarningState.AsWarning, wm.GetState(i));
            }

            // change the state of a warning
            wm.SetState(123, WarningState.Suppressed);
            Assert.Equal(WarningState.AsWarning, wm.GetState(122));
            Assert.Equal(WarningState.Suppressed, wm.GetState(123));
            Assert.Equal(WarningState.AsWarning, wm.GetState(124));

            // make sure suppression trumps warnings as errors
            wm.AllWarningsAreErrors = true;
            Assert.Equal(WarningState.Suppressed, wm.GetState(123));
            wm.AllWarningsAreErrors = false;

            // make sure warning as error doesn't mess up error state
            wm.SetState(321, WarningState.AsError);
            Assert.Equal(WarningState.AsError, wm.GetState(321));
            wm.AllWarningsAreErrors = true;
            Assert.Equal(WarningState.AsError, wm.GetState(321));
            wm.AllWarningsAreErrors = false;
            Assert.Equal(WarningState.AsError, wm.GetState(321));
        }
    }
}
