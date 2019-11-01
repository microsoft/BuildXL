// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// BuildXL.Execution.Analyzer entry point
    /// </summary>
    internal sealed class Program : ToolProgram<Args>
    {
        private Program()
            : base("BxlAnalyzer")
        { }

        public static int Main(string[] arguments)
        {
            try
            {
                return new Program().MainHandler(arguments);
            }
            catch (InvalidArgumentException e)
            {
                Console.Error.WriteLine("Execution error: " + (e.InnerException ?? e).Message);
            }

            return -1;
        }

        public override bool TryParse(string[] rawArgs, out Args arguments)
        {
            // If there are any exceptions with parsing arguments,
            // allow the program to exit
            arguments = new Args(rawArgs);
            return true;
        }

        public override int Run(Args arguments)
        {
            if (arguments.Help)
            {
                return 0;
            }

            return arguments.Analyze();
        }
    }
}
