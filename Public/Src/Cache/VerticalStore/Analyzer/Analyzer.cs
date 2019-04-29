// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.ToolSupport;
using BuildXL.Utilities;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// Cache.Analyzer entry point
    /// </summary>
    internal sealed class Analyzer : ToolProgram<Args>
    {
        private Analyzer()
            : base("CA") { }

        public static int Main(string[] arguments)
        {
            return new Analyzer().MainHandler(arguments);
        }

        public override bool TryParse(string[] rawArgs, out Args arguments)
        {
            try
            {
                arguments = new Args(rawArgs);
                return true;
            }
            catch (Exception ex)
            {
                ConsoleColor original = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.GetLogEventMessage());
                Console.ForegroundColor = original;
                arguments = null;
                return false;
            }
        }

        public override int Run(Args arguments)
        {
            using (arguments)
            {
                if (arguments.Help)
                {
                    return 0;
                }
                else
                {
                    return arguments.RunAnalyzer();
                }
            }
        }
    }
}
