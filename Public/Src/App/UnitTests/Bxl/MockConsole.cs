// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL
{
    internal class MockConsole : IConsole
    {
        private MessageLevel m_lastMessageLevel;
        // This list is used in ADOConsole for DX64 errors where the message is split into various segments and logged to ensure that only a part of the error message is highlighted.
        internal List<string> Messages = new List<string>();

        public void Dispose()
        {
        }

        public void ReportProgress(ulong done, ulong total)
        {
        }

        public void WriteOutputLine(MessageLevel messageLevel, string line)
        {
            m_lastMessageLevel = messageLevel;
            Messages.Add(line);
        }

        public void ValidateCall(MessageLevel messageLevel, string lineEnd)
        {
            ValidateCall(messageLevel, null, lineEnd);
        }

        public void ValidateCall(MessageLevel messageLevel, string lineStart, string lineEnd)
        {
            XAssert.IsNotNull(Messages.LastOrDefault(), "WriteOutputLine was not called");
            XAssert.AreEqual(messageLevel, m_lastMessageLevel);

            if (lineStart != null)
            {
                Assert.StartsWith(lineStart, Messages.LastOrDefault());
            }

            if (lineEnd != null)
            {
                Assert.EndsWith(lineEnd, Messages.LastOrDefault());
            }

            Messages.Clear();
        }

        public void ValidateCallForPipProcessEventinADO(MessageLevel messageLevel, List<string> expectedErrorMessage)
        {
            XAssert.IsNotNull(Messages.LastOrDefault(), "WriteOutputLine was not called");
            XAssert.AreEqual(messageLevel, m_lastMessageLevel);

            XAssert.IsTrue(expectedErrorMessage.SequenceEqual(Messages));

            Messages.Clear();
        }


        /// <summary>
        /// Check that no messages were printed on the Console
        /// </summary>
        public void ValidateNoCall()
        {
            XAssert.IsNull(Messages.LastOrDefault(), "Console printed a message while it was not supposed to do it.");
        }

        public void WriteOverwritableOutputLine(MessageLevel messageLevel, string standardLine, string overwritableLine)
        {
            // noop
        }

        public void WriteOverwritableOutputLineOnlyIfSupported(MessageLevel messageLevel, string standardLine, string overwritableLine)
        {
            // noop
        }

        public bool AskUser()
        {
            return false;
        }
    }
}
