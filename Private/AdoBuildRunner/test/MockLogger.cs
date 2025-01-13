// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.AdoBuildRunner.Vsts;
using Xunit;

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
#pragma warning disable xUnit2012 // Do not use boolean check to check if a value exists in a collection
            Assert.True(Messages.Any(m => m.Contains(substring)));
#pragma warning restore xUnit2012 // Do not use boolean check to check if a value exists in a collection
        }

        public void AssertLogNotContains(string substring)
        {
#pragma warning disable xUnit2012 // Do not use boolean check to check if a value exists in a collection
            Assert.False(Messages.Any(m => m.Contains(substring)));
#pragma warning restore xUnit2012 // Do not use boolean check to check if a value exists in a collection
        }

        public int MessageCount(string messageSubString)
        {
            var messageCount = 0;

            foreach (var message in Messages)
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