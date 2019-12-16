// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
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
        private string m_lastLine;

        public void Dispose()
        {
        }

        public void ReportProgress(ulong done, ulong total)
        {
        }

        public void WriteOutputLine(MessageLevel messageLevel, string line)
        {
            m_lastMessageLevel = messageLevel;
            m_lastLine = line;
        }

        public void ValidateCall(MessageLevel messageLevel, string lineEnd)
        {
            ValidateCall(messageLevel, null, lineEnd);
        }

        public void ValidateCall(MessageLevel messageLevel, string lineStart, string lineEnd)
        {
            XAssert.IsNotNull(m_lastLine, "WriteOutputLine was not called");
            XAssert.AreEqual(messageLevel, m_lastMessageLevel);

            if (lineStart != null)
            {
                Assert.StartsWith(lineStart, m_lastLine);
            }

            if (lineEnd != null)
            {
                Assert.EndsWith(lineEnd, m_lastLine);
            }

            m_lastLine = null;
        }

        /// <summary>
        /// Check that no messages were printed on the Console
        /// </summary>
        public void ValidateNoCall()
        {
            XAssert.IsNull(m_lastLine, "Console printed a message while it was not supposed to do it.");
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
