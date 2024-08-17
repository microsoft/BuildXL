// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildXL;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Strings = BuildXL.ConsoleLogger.Strings;

namespace Test.BuildXL
{
    internal class MockConsole : IConsole
    {
        private MessageLevel m_lastMessageLevel;

        /// <summary>
        /// This list is used to capture the console messages emitted.
        /// </summary>        
        internal List<string> Messages = new List<string>();

        /// <summary>
        /// This flag is used to mimic the StandardConsole. The flag is enabled when the fancyConsole option is enabled.
        /// </summary>
        public bool UpdatingConsole { get; set; } = false;

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

        public void ValidateCallForPipProcessEventinADO(MessageLevel messageLevel, List<string> expectedEventMessage)
        {
            XAssert.IsNotNull(Messages.LastOrDefault(), "WriteOutputLine was not called");
            XAssert.AreEqual(messageLevel, m_lastMessageLevel);

            if (!expectedEventMessage.SequenceEqual(Messages))
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Messages do not match. Expected message ({expectedEventMessage.Count} lines):");
                foreach (var line in expectedEventMessage)
                {
                    sb.AppendLine("[");
                    sb.AppendLine(line);
                    sb.AppendLine("]");
                }

                sb.AppendLine();
                sb.AppendLine($"Actual message ({Messages.Count} lines):");
                foreach (var line in Messages)
                {
                    sb.AppendLine("[");
                    sb.AppendLine(line);
                    sb.AppendLine("]");
                }

                XAssert.Fail(sb.ToString());
            }

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
            // We expect BackgroundTaskConsoleStatusMessage to be captured when the build is complete but service pips are still running, applicable for fancyConsole or ADO console.
            Messages.Add(UpdatingConsole ? overwritableLine : standardLine);
        }

        public void WriteOverwritableOutputLineOnlyIfSupported(MessageLevel messageLevel, string standardLine, string overwritableLine)
        {
            // noop
        }

        public bool AskUser()
        {
            return false;
        }

        /// <summary>
        /// Validates console build status message for service pips.
        /// BackgroundTaskConsoleStatusMessage is shown only when build completes while service pips run, applicable to ADO console or with fancyConsole option.
        /// </summary>
        public void ValidateBuildStatusLineMessage(bool expectedBuildStatusMessage)
        {
            XAssert.IsNotNull(Messages.LastOrDefault(), "WriteOutputLine was not called");
            var containsMessage = Messages.LastOrDefault()?.Contains(Strings.BackgroundTaskConsoleStatusMessage) ?? false;
            XAssert.AreEqual(expectedBuildStatusMessage, containsMessage);
            Messages.Clear();
        }

        /// <inheritdoc/>
        public void SetRecoverableErrorAction(Action<Exception> errorAction)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void WriteHyperlink(MessageLevel messageLevel, string text, string target)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void WriteOutput(MessageLevel messageLevel, string text)
        {
            throw new NotImplementedException();
        }
    }
}
