// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
                Console.Error.WriteLine("Execution error due to InvalidArgumentException: " + e);
                return (int)ExitCode.InvalidArgument;
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch (Exception e)
            {
                // To convert all bxl crashes to build failures.
                // Logs the exception text to help developers while debugging crashes.
                Console.Error.WriteLine("Execution error: " + e);
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            return (int)ExitCode.InternalError;
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
