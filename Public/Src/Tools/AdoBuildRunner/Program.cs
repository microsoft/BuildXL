// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AdoBuildRunner;
using AdoBuildRunner.Vsts;
using BuildXL.AdoBuildRunner.Build;
using BuildXL.AdoBuildRunner.Vsts;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;
using MachineRole = AdoBuildRunner.MachineRole;

#nullable enable

namespace BuildXL.AdoBuildRunner
{
    class Program
    {
        /// <summary>
        ///  To run a distributed build, the AdoBuildRunnerWorkerPipelineRole environment variable should
        ///  be set to either "Orchestrator" or "Worker". An unset or different value will result in a single-machine build. 
        ///      
        ///  All of the arguments passed to the AdoBuildRunner will be used as arguments for the BuildXL invocation.
        ///  For the orchestrator build, the /dynamicBuildWorkerSlots argument should be passed with the number of
        ///  remote workers that this build expects. The rest of the arguments for distributed builds are chosen
        ///  by the build runner (see <see cref="BuildExecutor"/>).
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            var logger = new Logger();

            // Adding a condition to ensure that we exit early if we are not running in ADO.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Constants.BuildIdVarName)))
            {
                logger.Error("The build is not running in ADO, aborting!");
                return 1;
            }

            if (args.Length == 0)
            {
                logger.Error("No build arguments have been supplied for coordination, aborting!");
                return 1;
            }

            if (Environment.GetEnvironmentVariable("AdoBuildRunnerDebugOnStart") == "1")
            {
                if (OperatingSystemHelper.IsUnixOS)
                {
                    Console.WriteLine("=== Attach to this process from a debugger, then press ENTER to continue ...");
                    Console.ReadLine();
                }
                else
                {
                    Debugger.Launch();
                }
            }

            try
            {
                logger.Info($"Trying to coordinate build for command: {string.Join(" ", args)}");
                IAdoEnvironment adoEnvironment = new AdoEnvironment(logger);
                // TODO: There are currently many arguments that are passed to the runner via environment variables. Fold them into
                // the configuration object and add explicit CLI arguments for them.
                if (!Args.TryParseArguments(logger, args, adoEnvironment, out var configuration, out var forwardingArguments))
                {
                    throw new InvalidArgumentException("Invalid command line option");
                }
                var buildArgs = forwardingArguments.ToList();

                var adoBuildRunnerService = new AdoBuildRunnerService(configuration, adoEnvironment, logger);
                await adoBuildRunnerService.GenerateCacheConfigFileIfNeededAsync(logger, configuration, buildArgs);
                var buildContext = await adoBuildRunnerService.GetBuildContextAsync(adoBuildRunnerService.GetInvocationKey());
                IBuildExecutor buildExecutor = adoBuildRunnerService.Config.PipelineRole switch
                {
                    MachineRole.Orchestrator => new OrchestratorBuildExecutor(logger, adoBuildRunnerService),
                    MachineRole.Worker => new WorkerBuildExecutor(logger, adoBuildRunnerService),
                    _ => throw new InvalidOperationException($"Invalid Machine Role")
                };
                var buildManager = new BuildManager(adoBuildRunnerService, buildExecutor, buildContext, buildArgs.ToArray(), logger);
                return await buildManager.BuildAsync();
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
