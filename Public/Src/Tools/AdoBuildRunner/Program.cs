// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using AdoBuildRunner.Vsts;
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
                logger.Error("No build arguments have been supplied for coordination, aborting!");
                return 1;
            }

            try
            {
                var api = new Api(logger);
                IBuildExecutor executor;
                if (args[0] == "ping")
                {
                    logger.Info("Performing connectivity test");
                    executor = new PingExecutor(logger, api);
                }
                else
                {
                    logger.Info($"Trying to coordinate build for command: {string.Join(" ", args)}");
                    executor = new BuildExecutor(logger);
                }

                var buildManager = new BuildManager(api, executor, args, logger);
                return buildManager.BuildAsync().GetAwaiter().GetResult();
            }
            catch (CoordinationException e)
            {
                logger.Error(e, "ADO build coordination failed, aborting!");
                return 1;
            }
            catch (Exception e)
            {
                logger.Error(e, "ADOBuildRunner failed, aborting!");
                return 1;
            }
        }
    }
}
