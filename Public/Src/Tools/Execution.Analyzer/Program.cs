// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;

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
            catch (Exception e)
            {
                Console.WriteLine($"An exception occurred during execution: {e.ToStringDemystified()}");
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
