// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.ToolSupport;

namespace BuildXL.SandboxedProcessExecutor
{
    internal sealed class Program : ToolProgram<Args>
    {
        private Program()
            : base("SandboxedProcessExecutor")
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

            return (int)ExitCode.InvalidArgument;
        }

        public override bool TryParse(string[] rawArgs, out Args arguments)
        {
            // If there is any exception with parsing arguments, allow the program to exit
            arguments = new Args(rawArgs);
            return true;
        }

        public override int Run(Args arguments)
        {
            bool success = arguments.TryProcess(out Configuration configuration);

            if (!success)
            {
                return (int)ExitCode.InvalidArgument;
            }

            if (arguments.Help)
            {
                return (int)ExitCode.Success;
            }

            var exitCode = new Executor(configuration).Run();
            return exitCode;
        }
    }
}
