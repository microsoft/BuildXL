// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.AdoBuildRunner.Build;
using BuildXL.AdoBuildRunner.Vsts;

namespace BuildXL.AdoBuildRunner
{
    class Program
    {
        static int Main(string[] args)
        {
            var logger = new Logger();

            if (args.Length == 0)
            {
                logger.Error("No build arguments have been supplied for orchestration, aborting!");
                return 1;
            }

            try
            {
                IBuildExecutor executor;
                if (args[0] == "ping")
                {
                    logger.Info("Performing connectivity test");
                    executor = new PingExecutor(logger);
                }
                else
                {
                    logger.Info($"Trying to orchestrate build for command: {string.Join(" ", args)}");
                    executor = new BuildExecutor(logger);
                }

                var buildManager = new BuildManager(new Api(logger), executor, args, logger);
                return buildManager.BuildAsync().GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                logger.Error(e, "VSTS build orchestration task execution failed, aborting!");
                return 1;
            }
        }
    }
}
