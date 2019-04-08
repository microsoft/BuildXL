// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

                List<LoggingSite> loggingSites;
                if (parser.DiscoverLoggingSites(out loggingSites))
                {
                    LogWriter writer = new LogWriter(config, errorReport);
                    int itemsWritten = writer.WriteLog(loggingSites);
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
