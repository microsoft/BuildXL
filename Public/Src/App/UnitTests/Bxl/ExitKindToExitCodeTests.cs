// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities.Configuration;
using Xunit;

namespace Test.BuildXL
{
    public class ExitKindToExitCodeTests
    {
        [Fact]
        public void AllExitKindsAccountedFor()
        {
            foreach (ExitKind exitKind in Enum.GetValues(typeof(ExitKind)))
            {
                // No crash = happy test
                ExitCode.FromExitKind(exitKind);
            }
        }
    }
}
