// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Testing.TestGenerator;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    /// <summary>
    /// Test logger
    /// </summary>
    public class TestLogger : Logger
    {
        private readonly List<string> m_errors = new List<string>();

        private ITestOutputHelper m_output;

        public TestLogger(ITestOutputHelper output)
        {
            m_output = output;
        }

        /// <inheritdoc />
        protected override void WriteMessage(string message)
        {
            m_output.WriteLine(message);
        }

        /// <inheritdoc />
        protected override void WriteError(string message)
        {
            m_errors.Add(message);
            m_output.WriteLine(message);
        }

        /// <summary>
        /// Validates that the error count matches and each expected message is a substring of at least one reported error.
        /// </summary>
        public void ValidateErrors(int expectedErrorCount, params string[] expectedMessages)
        {
            Assert.Equal(expectedErrorCount, ErrorCount);

            foreach (var expectedMessage in expectedMessages)
            {
                bool found = false;
                foreach (var error in m_errors)
                {
                    if (error.IndexOf(expectedMessage, StringComparison.Ordinal) >= 0)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    var lines = string.Join("\n* ", m_errors);
                    XAssert.Fail(I($"Did not find message '{expectedMessage}':\n* {lines}"));
                }
            }
        }
    }
}
