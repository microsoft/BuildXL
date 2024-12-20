// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;
using BuildXL.AdoBuildRunner.Utilties.Mocks;
using System.Linq;

namespace BuildXL.AdoBuildRunner
{
    internal sealed class Program
    {
        /// <nodoc />
        public static async Task<int> Main(string[] arguments)
        {
            if (Environment.GetEnvironmentVariable("AdoBuildRunnerDebugOnStart") == "1")
            {
                System.Diagnostics.Debugger.Launch();
            }

            var logger = new Logger();

            // Look for the "--" argument which separates 'runner' from 'buildXL' arguments
            var indexOfSeparator = -1;
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].Trim() == "--")
                {
                    indexOfSeparator = i;
                    break;
                }
            }

            string[] runnerArguments;
            string[] forwardingArguments;

            if (indexOfSeparator == -1)
            {
                // TODO: Make this an error.
                // For now, we have callers that don't use the separator, in that
                // case we assume that all arguments are 'forwarding argument'
                runnerArguments = [];
                forwardingArguments = arguments;
                // logger.Error($"Usage: ./AdoBuildRunner{(OperatingSystem.IsWindows() ? ".exe" : "")} [runner arguments] -- [buildxl arguments]");
                // return 1;
            }
            else
            {

                runnerArguments = TransformBackwardsCompatible(arguments[0..indexOfSeparator]);
                forwardingArguments = arguments[(indexOfSeparator + 1)..];
            }
            var argumentParserCommand = new RootCommand("Build tools installer");

            // Initialize to -1 to make the compiler happy:
            // we this should assign every time in the regular execution flow below
            IAdoBuildRunnerConfiguration config = null!;
            argumentParserCommand.SetHandler(parsedArgs =>
            {
                config = parsedArgs;
            }, new ArgumentBinder(argumentParserCommand));


            var parsingResult = await argumentParserCommand.InvokeAsync(runnerArguments);
            if (parsingResult != 0)
            {
                return parsingResult;
            }

            return await RunAsync(config, forwardingArguments.ToList(), logger);
        }

        /// <summary>
        /// Transform arguments of the form /foo:bar into --foo bar
        /// to transition between the old version of argument parsing
        /// and the new one using System.CommandLine
        /// </summary>
        internal static string[] TransformBackwardsCompatible(string[] args)
        {
            var result = new List<string>();

            foreach (var s in args)
            {
                // Change leading / to --
                var cleanedPrefix = s.Trim();
                if (cleanedPrefix.StartsWith('/'))
                {
                    cleanedPrefix = "--" + cleanedPrefix.Substring(1);
                }

                // If there is a colon, after the colon is the provided value
                var splitOnColon = cleanedPrefix.Split(':', 2);
                foreach (var resultingArg in splitOnColon) 
                {
                    result.Add(resultingArg);
                }
            }

            return result.ToArray();
        }

        private static async Task<int> RunAsync(IAdoBuildRunnerConfiguration configuration, List<string> buildArgs, ILogger logger)
        {
            try
            {
                IAdoEnvironment adoEnvironment;
#if DEBUG
                if (Environment.GetEnvironmentVariable("__ADOBR_INTERNAL_MOCK_ADO") == "1")
                {
                    adoEnvironment = new MockAdoEnvironment();
                }
                else
                {
                    adoEnvironment = new AdoEnvironment(logger);
                }
#else
                adoEnvironment = new AdoEnvironment(logger);
#endif
                // TODO: There are currently many arguments that are passed to the runner via environment variables.
                // For now, fall back to these, if defined:
                configuration.PopulateFromEnvVars(adoEnvironment, logger);

                var adoBuildRunnerService = new AdoBuildRunnerService(configuration, adoEnvironment, logger);
                await AdoBuildRunnerService.GenerateCacheConfigFileIfNeededAsync(logger, configuration.CacheConfigGenerationConfiguration, buildArgs);

                var buildXLLauncher = new BuildXLLauncher(configuration);
                IBuildExecutor buildExecutor = adoBuildRunnerService.Config.AgentRole switch
                {
                    AgentRole.Orchestrator => new OrchestratorBuildExecutor(buildXLLauncher, adoBuildRunnerService, logger),
                    AgentRole.Worker => new WorkerBuildExecutor(buildXLLauncher, adoBuildRunnerService, logger),
                    _ => throw new InvalidOperationException($"Invalid Machine Role: {adoBuildRunnerService.Config.AgentRole}")
                };
                var buildManager = new BuildManager(adoBuildRunnerService, buildExecutor, buildArgs.ToArray(), logger);
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
