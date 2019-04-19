// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.ToolSupport;

namespace BuildXL.SandboxedProcessExecutor
{
    /// <summary>
    /// Commmand line argument parser.
    /// </summary>
    internal sealed class Args : CommandLineUtilities
    {
        private static readonly string[] s_helpStrings = new[] { "?", "help" };
        public bool Help;

        /// <summary>
        /// Initializes and parses command line arguments.
        /// </summary>
        public Args(string[] args)
            : base(args)
        {
        }

        /// <summary>
        /// Processes command line arguments to construct an instance of <see cref="Configuration"/>.
        /// </summary>
        public bool TryProcess(out Configuration configuration)
        {
            configuration = new Configuration();

            foreach (Option option in Options)
            {
                if (OptionEquals(option, "sandboxedProcessInfo") || OptionEquals(option, "i"))
                {
                    configuration.SandboxedProcessInfoInputFile = ParsePathOption(option);
                }
                else if (OptionEquals(option, "sandboxedProcessResult") || OptionEquals(option, "r"))
                {
                    configuration.SandboxedProcessResultOutputFile = ParsePathOption(option);
                }
                else if (OptionEquals(option, "enableTelemetry"))
                {
                    configuration.EnableTelemetry = ParseBooleanOption(option);
                }
                else if (s_helpStrings.Any(s => OptionEquals(option, s)))
                {
                    // If the analyzer was called with '/help' argument - print help and exit
                    Help = true;
                    WriteHelp();
                    return true;
                }
                else
                {
                    Console.Error.WriteLine($"Option '{option.Name}' is not recognized");
                    return false;
                }
            }

            if (!configuration.Validate(out string errorConfiguration))
            {
                Console.Error.WriteLine($"Configuration error: {errorConfiguration}");
                return false;
            }

            return true;
        }

        private bool OptionEquals(Option option, string name) => option.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

        private void WriteHelp()
        {
            HelpWriter writer = new HelpWriter();
            writer.WriteBanner("Tool for executing and monitoring process in a sandbox");

            writer.WriteLine("");
            writer.WriteLine("/sandboxedProcessInfo:<file>   -- Sandboxed process info input file [short: /i]");
            writer.WriteLine("/sandboxedProcessResult:<file> -- Sandboxed process result output file [short: /r]");
            writer.WriteLine("/sandboxedProcessResult:<file> -- Sandboxed process result output file [short: /r]");
            writer.WriteLine("/enableTelemetry[+/-]          -- Enable telemetry [default: false]");
            writer.WriteLine("/help                          -- Print help message and exit [short: /?]");
        }
    }
}
