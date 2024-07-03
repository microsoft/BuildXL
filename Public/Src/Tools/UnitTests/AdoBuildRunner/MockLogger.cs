// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.AdoBuildRunner.Vsts;

namespace Test.Tool.AdoBuildRunner
{
    public class MockLogger : ILogger
    {
        internal List<string> Messages = new List<string>();

        /// <nodoc />
        public void Info(string message)
        {
            Console.WriteLine(WithTimeStamp(message));
            Messages.Add(message);
        }

        /// <nodoc />
        public void Debug(string debugMessage)
        {

        }

        /// <nodoc />
        public void Warning(string warningMessage)
        {

        }

        /// <nodoc />
        public void Error(string errorMessage)
        {

        }

        /// <nodoc />
        public void Warning(string format, params object[] args)
        {

        }

        /// <nodoc />
        public void Error(string format, params object[] args)
        {

        }

        /// <nodoc />
        public void Debug(string format, params object[] args)
        {

        }

        /// <nodoc />
        public void Warning(Exception ex, string warningMessage)
        {

        }

        /// <nodoc />
        public void Error(Exception ex, string errorMessage)
        {

        }

        private string WithTimeStamp(string message) => string.Format("[{0}] {1}", DateTime.UtcNow.ToString("HH:mm:ss.ff"), message);

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