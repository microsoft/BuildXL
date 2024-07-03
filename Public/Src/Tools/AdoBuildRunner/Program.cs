// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AdoBuildRunner.Vsts;
using BuildXL.AdoBuildRunner.Build;
using BuildXL.AdoBuildRunner.Vsts;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;

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
                var adoBuildRunnerService = new AdoBuildRunnerService(logger);

                IBuildExecutor executor;
                if (args[0] == "ping")
                {
                    // ping mode - for debugging purposes
                    var buildContext = await adoBuildRunnerService.GetBuildContextAsync("ping");
                    logger.Info("Performing connectivity test");
                    executor = new PingExecutor(logger, adoBuildRunnerService);
                    var buildManager = new BuildManager(adoBuildRunnerService, executor, buildContext, args, logger);
                    return await buildManager.BuildAsync(isOrchestrator: Environment.GetEnvironmentVariable(Constants.AdoBuildRunnerPipelineRole) == "Orchestrator");
                }
                else
                {
                    logger.Info($"Trying to coordinate build for command: {string.Join(" ", args)}");

                    // TODO: There are currently many arguments that are passed to the runner via environment variables. Fold them into
                    // the configuration object and add explicit CLI arguments for them.
                    if (!Args.TryParseArguments(logger, args, out var configuration, out var forwardingArguments))
                    {
                        throw new InvalidArgumentException("Invalid command line option");
                    }

                    var buildArgs = forwardingArguments.ToList();
                    await GenerateCacheConfigFileIfNeededAsync(logger, configuration, buildArgs);

                    // Carry out the build.
                    // For now, we explicitly mark the role with an environment
                    // variable. When we discontinue the "non-worker-pipeline" approach, we can
                    // infer the role from the parameters, but for now there is no easy way
                    // to distinguish runs using the "worker-pipeline" model from ones who don't
                    var role = Environment.GetEnvironmentVariable(Constants.AdoBuildRunnerPipelineRole);

                    executor = new BuildExecutor(logger);

                    bool isOrchestrator;
                    if (string.IsNullOrEmpty(role))
                    {
                        // When the build role is not specified, we assume this build is being run with the parallel strategy
                        // where the role is inferred from the ordinal position in the phase: the first agent is the orchestrator
                        isOrchestrator = adoBuildRunnerService.JobPositionInPhase == 1;
                    }
                    else
                    {
                        bool isWorker = string.Equals(role, "Worker", StringComparison.OrdinalIgnoreCase);
                        isOrchestrator = string.Equals(role, "Orchestrator", StringComparison.OrdinalIgnoreCase);
                        if (!isWorker && !isOrchestrator)
                        {
                            throw new CoordinationException($"{Constants.AdoBuildRunnerPipelineRole} must be 'Worker' or 'Orchestrator'");
                        }
                    }

                    // A build key has to be specified to disambiguate between multiple builds
                    // running as part of the same pipeline. This value is used to communicate
                    // the build information (orchestrator location, session id) to the workers. 
                    var invocationKey = Environment.GetEnvironmentVariable(Constants.AdoBuildRunnerInvocationKey);

                    if (string.IsNullOrEmpty(invocationKey))
                    {
                        throw new CoordinationException($"The environment variable {Constants.AdoBuildRunnerInvocationKey} must be set (to a value that is unique within a particular pipeline run): " +
                            $"it is used to disambiguate between multiple builds running as part of the same pipeline (e.g.: debug, ship, test...) " +
                            $"and to communicate the build information to the worker pipeline");
                    }

                    var attemptNumber = Environment.GetEnvironmentVariable(Constants.JobAttemptVariableName) ?? "1";
                    if (int.TryParse(attemptNumber, out var jobAttempt) && jobAttempt > 1)
                    {
                        // The job was rerun. Let's change the invocation key to reflect that
                        // so we don't conflict with the first run.
                        invocationKey += $"__jobretry_{jobAttempt}";
                    }

                    var buildContext = await adoBuildRunnerService.GetBuildContextAsync(invocationKey);

                    var buildManager = new BuildManager(adoBuildRunnerService, executor, buildContext, buildArgs.ToArray(), logger);
                    return await buildManager.BuildAsync(isOrchestrator);
                }
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

        private static async Task GenerateCacheConfigFileIfNeededAsync(Logger logger, IAdoBuildRunnerConfiguration configuration, List<string> buildArgs)
        {
            // If the storage account endpoint was provided, that is taken as an indicator that the cache config needs to be generated
            if (configuration.CacheConfigGenerationConfiguration.StorageAccountEndpoint == null)
            {
                return;
            }

            string cacheConfigContent = CacheConfigGenerator.GenerateCacheConfig(configuration.CacheConfigGenerationConfiguration);
            string cacheConfigFilePath = Path.GetTempFileName();

            await File.WriteAllTextAsync(cacheConfigFilePath, cacheConfigContent);
            logger.Info($"Cache config file generated at '{cacheConfigFilePath}'");

            if (configuration.CacheConfigGenerationConfiguration.LogGeneratedConfiguration())
            {
                logger.Info($"Generated cache config file: {cacheConfigContent}");
            }

            // Inject the cache config file path as the first argument in the build arguments. This is so if there is any user override pointing to another
            // cache config file, that will be honored
            buildArgs.Insert(0, $"/cacheConfigFilePath:{cacheConfigFilePath}");
        }
    }
}
