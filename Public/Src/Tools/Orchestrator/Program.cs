// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Orchestrator.Vsts;
using BuildXL.Orchestrator.Build;

namespace BuildXL.Orchestrator
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
                logger.Info($"Trying to orchestrate build for command: {string.Join(" ", args)}");
                var buildManager = new BuildManager(new Api(logger), new BuildExecutor(logger), args, logger);
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
