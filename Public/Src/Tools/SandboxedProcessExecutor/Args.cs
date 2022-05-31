// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
                else if (OptionEquals(option, "remoteSandboxedProcessData"))
                {
                    configuration.RemoteSandboxedProcessDataFile = ParsePathOption(option);
                }
                else if (OptionEquals(option, "remoteArgSalt"))
                {
                    // Remoting engine can use command-line argument to perform cache operation.
                    // For example, in AnyBuild although Action cache is most-likely disabled, AnyBuild
                    // service can use the command-line argument to get cached historic information for VFS pre-rendering.
                    // To avoid getting a cache hit, without modifying/re-deploying AnyBuild service we can use this option to make
                    // the command-line argument different.

                    // Simply eat up the string salt.
                    ParseStringOption(option);
                }
                else if (OptionEquals(option, "enableTelemetry"))
                {
                    configuration.EnableTelemetry = ParseBooleanOption(option);
                }
                else if (OptionEquals(option, "printObservedAccesses"))
                {
                    configuration.PrintObservedAccesses = ParseBooleanOption(option);
                }
                else if (OptionEquals(option, "testHook"))
                {
                    configuration.SandboxedProcessExecutorTestHookFile = ParsePathOption(option);
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

        private static bool OptionEquals(Option option, string name) => option.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

        private static void WriteHelp()
        {
            HelpWriter writer = new HelpWriter();
            writer.WriteBanner("Tool for executing and monitoring process in a sandbox");

            writer.WriteLine("");
            writer.WriteLine("/sandboxedProcessInfo:<file>             -- Sandboxed process info input file [short: /i]");
            writer.WriteLine("/sandboxedProcessResult:<file>           -- Sandboxed process result output file [short: /r]");
            writer.WriteLine("/remoteSandboxedProcessData:<file>       -- Sideband data for process remoting");
            writer.WriteLine("/remoteArgSalt:<string>                  -- Salt to make the command-line argument different");
            writer.WriteLine("/enableTelemetry[+/-]                    -- Enable telemetry [default: false]");
            writer.WriteLine("/printObservedAccesses[+/-]              -- Print observed file/directory accesses [default: false]");
            writer.WriteLine("                                            ReportedFileAccesses in file manifest must be set to true");
            writer.WriteLine("/help                                    -- Print help message and exit [short: /?]");
        }
    }
}
