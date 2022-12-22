// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using AdoBuildRunner.Vsts;
using BuildXL.AdoBuildRunner.Build;
using BuildXL.AdoBuildRunner.Vsts;

#nullable enable

namespace BuildXL.AdoBuildRunner
{
    class Program
    {
        /// <summary>
        /// The AdoBuildRunner supports two modes of operation (apart from a ping mode only used for debugging)
        ///   
        /// (1) launchworkers mode. This is used to trigger a distributed worker pipeline.
        ///     This mode is chosen if the first argument to the program is exactly "launchworkers".
        ///     It is important that this invocation is made from the same job that will run the build:
        ///     this is both because we communicate the agent's IP address at this stage and also calculate
        ///     the related session ID as a hash of job-specific values.
        ///
        ///     The command line should look like this:
        ///         AdoBuildRunner.exe launchworkers 12345 [/param:key1=value1 /var:key2=value2 ...]
        ///     Where:
        ///         12345 is is the pipeline id that we are about to queue. This should always be the second argument.
        ///         After that, a number of /param or /var options can be passed. A /param option specifies a
        ///         template parameter, and a /var option a build variable, that we will send along with the build request.
        ///         All of these parameters should be settable at queue time or the build trigger will fail
        ///
        /// (2) build mode. 
        ///       If the first argument is neither "ping" or "launchworkers", the AdoBuildRunner will try to run 
        ///       a BuildXL build. 
        ///       To run a distributed build, the AdoBuildRunnerWorkerPipelineRole environment variable should
        ///       be set to either "Orchestrator" or "Worker". An unset or different value will result in a single-machine build. 
        ///      
        ///       All of the arguments passed to the AdoBuildRunner will be used as arguments for the BuildXL invocation.
        ///       For the orchestrator build, the /dynamicBuildWorkerSlots argument should be passed with the number of
        ///       remote workers that this build expects. The rest of the arguments for distributed builds are chosen
        ///       by the build runner (see <see cref="BuildExecutor"/>).
        ///       
        /// </summary>
        public static async Task<int> Main(string[] args)
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

                var buildContext = await api.GetBuildContextAsync();

                IBuildExecutor executor;
                if (args[0] == "ping")
                {
                    // ping mode - for debugging purposes
                    logger.Info("Performing connectivity test");
                    executor = new PingExecutor(logger, api);
                    var buildManager = new BuildManager(api, executor, args, logger);
                    return await buildManager.BuildAsync(buildContext);
                }
                else if (args[0] == "launchworkers")
                {
                    if (args.Length < 2 || !int.TryParse(args[1], out var pipelineId))
                    {
                        throw new CoordinationException("launchworkers mode's first argument must be an integer representing the worker pipeline id");
                    }

                    var wq = new WorkerQueuer(buildContext, logger, api);
                    await wq.QueueWorkerPipelineAsync(pipelineId, args.Skip(2).ToArray());
                    return 0;
                }
                else
                {
                    logger.Info($"Trying to coordinate build for command: {string.Join(" ", args)}");
                    
                    // Carry out the build.
                    // For now, we explicitly mark the role with an environment
                    // variable. When we discontinue the "non-worker-pipeline" approach, we can
                    // infer the role from the parameters, but for now there is no easy way
                    // to distinguish runs using the "worker-pipeline" model from ones who don't
                    var role = Environment.GetEnvironmentVariable(Constants.AdoBuildRunnerPipelineRole);
                    executor = new BuildExecutor(logger);

                    if (string.IsNullOrEmpty(role))
                    {
                        var buildManager = new BuildManager(api, executor, args, logger);
                        return await buildManager.BuildAsync(buildContext);
                    }
                    else 
                    {
                        bool isWorker = string.Equals(role, "Worker", StringComparison.OrdinalIgnoreCase);
                        bool isOrchestrator = string.Equals(role, "Orchestrator", StringComparison.OrdinalIgnoreCase);
                        if (!isWorker && !isOrchestrator)
                        {                        
                            throw new CoordinationException($"{Constants.AdoBuildRunnerPipelineRole} must be 'Worker' or 'Orchestrator'");
                        }

                        var buildManager = new WorkerPipelineBuildManager(api, executor, buildContext, args, logger);
                        return await buildManager.BuildAsync(isOrchestrator);
                    }
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
    }
}
