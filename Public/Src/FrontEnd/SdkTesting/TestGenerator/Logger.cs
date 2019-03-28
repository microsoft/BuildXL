// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    /// <summary>
    /// Simple logger class that logs to the console
    /// </summary>
    /// <remarks>
    /// This has virtual methods so unit tests can derive and collect the errors.
    /// </remarks>
    public class Logger
    {
        /// <summary>
        /// The number of errors logged
        /// </summary>
        public int ErrorCount { get; private set; }

        /// <summary>
        /// Writes out the message
        /// </summary>
        protected virtual void WriteMessage(string message)
        {
            Console.WriteLine(message);
        }

        /// <summary>
        /// Writes out an error
        /// </summary>
        protected virtual void WriteError(string message)
        {
            Console.Error.WriteLine(message);
        }

        /// <summary>
        /// Log a message to stdout
        /// </summary>
        public void LogMessage(string message)
        {
            WriteMessage(message);
        }

        /// <summary>
        /// Log an error message without location information
        /// </summary>
        public void LogError(string message)
        {
            WriteError("ERROR: " + message);
            ErrorCount++;
        }

        /// <summary>
        /// Log an error message with a location marker information
        /// </summary>
        public void LogError(ISourceFile sourceFile, INode node, string message)
        {
            var file = sourceFile.FileName;
            var location = node.GetLineInfo(sourceFile);

            WriteError(I($"{file}({location.Line},{location.Position}): Error: {message}"));
            ErrorCount++;
        }

        /// <summary>
        /// Log an error message with a location marker information
        /// </summary>
        public void LogError(string file, int line, int column, string message)
        {
            WriteError(I($"{file}({line},{column}): Error: {message}"));
            ErrorCount++;
        }
    }
}
