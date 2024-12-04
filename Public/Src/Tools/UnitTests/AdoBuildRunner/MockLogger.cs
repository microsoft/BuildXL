// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.AdoBuildRunner.Vsts;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.Tool.AdoBuildRunner
{
    public class MockLogger : Logger
    {
        internal List<string> Messages = new List<string>();

        /// <inheritdoc />
        protected override void Output(string message)
        {
            Messages.Add(message);
        }

        public void AssertLogContains(string substring)
        {
            XAssert.IsTrue(Messages.Any(m => m.Contains(substring)));
        }

        public int MessageCount(string messageSubString)
        {
            var messageCount = 0;

            foreach(var message in Messages)
            {
                if (message.Contains(messageSubString))
                {
                    messageCount++;
                }
            }

            return messageCount;
        }
    }
}