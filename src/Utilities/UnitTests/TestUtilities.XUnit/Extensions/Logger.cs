// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// Special logger type used by Xunit extension points.
    /// </summary>
    internal sealed class Logger
    {
        /// <summary>
        /// Special marker that is recognized by the error regex to print to the output only messages that starts with this prefix.
        /// </summary>
        private const string ErrorMarkerPrefix = " \b";

        /// <summary>
        /// Synchronization root for a logger.
        /// </summary>
        public object LockObject { get; } = new object();

        /// <summary>
        /// Prints a verbose message that will be filtered out by the regex but will be preserved in the full output.
        /// </summary>
        /// <param name="message"></param>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public void LogVerbose(string message)
        {
            Console.WriteLine(message);
        }

        // All the next functions are used to print a message of the failed test case.
        // These are the same methods that Xunit IRunnerLogger has.

        /// <nodoc />
        public void LogImportantMessage(string message) => LogMessageWithColor(ConsoleColor.Gray, message);

        /// <nodoc />
        public void LogError(string message) => LogMessageWithColor(ConsoleColor.Red, message);

        /// <nodoc />
        public void LogMessage(string message) => LogMessageWithColor(ConsoleColor.DarkGray, message);

        private void LogMessageWithColor(ConsoleColor color, string message)
        {
            lock (LockObject)
            {
                using (SetColor(color))
                {
                    Console.WriteLine($"{ErrorMarkerPrefix}{message}");
                }
            }
        }

        private static ColorRestorer SetColor(ConsoleColor color) => new ColorRestorer(color);

        private readonly struct ColorRestorer : IDisposable
        {
            private readonly ConsoleColor m_color;
            public ColorRestorer(ConsoleColor color)
            {
                m_color = color;
                Console.ForegroundColor = color;
            }

            public void Dispose()
                => Console.ForegroundColor = m_color;
        }
    }
}
