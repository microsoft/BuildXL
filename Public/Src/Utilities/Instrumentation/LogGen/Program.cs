// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BuildXL.LogGen
{
    /// <summary>
    /// BuildXL log generator
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point
        /// </summary>
        public static void Main(string[] args)
        {
            try
            {
                if (Environment.GetEnvironmentVariable("BuildXLLogGenDebugOnStart") == "1")
                {
                    Debugger.Launch();
                }

                ErrorReport errorReport = new ErrorReport();
                Configuration config = new Configuration(args);
                Parser parser = new Parser(config, errorReport);

                if (parser.DiscoverLoggingSites(out var loggingClasses))
                {
                    LogWriter writer = new LogWriter(config, errorReport);
                    int itemsWritten = writer.WriteLog(loggingClasses);
                    if (itemsWritten == 0)
                    {
                        Console.Error.WriteLine("No log helpers to be written. Turn off log generation.");
                        Environment.Exit(2);
                    }
                }

                Environment.Exit(errorReport.Errors == 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Environment.Exit(1);
            }
        }
    }
}
