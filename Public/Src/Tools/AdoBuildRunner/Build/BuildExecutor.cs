// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;

#nullable enable

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// A build executor that can execute a build engine depending on agent status and build arguments
    /// </summary>
    public abstract class BuildExecutor : IBuildExecutor
    {
        private readonly string m_bxlExeLocation;
        /// <nodoc />
        protected readonly IAdoBuildRunnerService AdoBuildRunnerService;
        /// <nodoc />
        protected readonly ILogger Logger;

        /// <nodoc />
        public BuildExecutor(ILogger logger, IAdoBuildRunnerService adoBuildRunnerService)
        {
            // Resolve the bxl executable location
            var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "bxl" : "bxl.exe";
            m_bxlExeLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, exeName);
            AdoBuildRunnerService = adoBuildRunnerService;
            Logger = logger;
        }

        /// <nodoc />
        protected int ExecuteBuild(BuildContext buildContext, IEnumerable<string> fullArguments, string buildSourceDirectory)
        {
            var process = new Process()
            {
                StartInfo =
                {
                    FileName = m_bxlExeLocation,
                    Arguments = ExtractAndEscapeCommandLineArguments(fullArguments),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
            };

            process.OutputDataReceived += ((sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Logger.Info(e.Data);
                }
            });

            process.ErrorDataReceived += ((sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Logger.Warning(e.Data);
                }
            });

            Logger.Info($"Launching File: {process.StartInfo.FileName} with Arguments: {process.StartInfo.Arguments}");

            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return process.ExitCode;
        } 

        /// <nodoc />
        private static string ExtractAndEscapeCommandLineArguments(IEnumerable<string> args) => string.Join(' ', args);

        /// <inheritdoc />
        public void PrepareBuildEnvironment(BuildContext buildContext)
        {
            if (buildContext == null)
            {
                throw new ArgumentNullException(nameof(buildContext));
            }
        }

        /// <inheritdoc />
        public int ExecuteSingleMachineBuild(BuildContext buildContext, string[] buildArguments)
        {
            Logger.Info($@"Launching single machine build");
            return ExecuteBuild(buildContext, buildArguments, buildContext.SourcesDirectory);
        }

        /// <inheritdoc />
        public abstract Task<int> ExecuteDistributedBuild(BuildContext buildContext, string[] buildArguments);

        /// <inheritdoc />
        public abstract string[] ConstructArguments(BuildContext buildContext, BuildInfo buildInfo, string[] buildArguments);

        /// <summary>
        /// Set arguments common to the worker and the orchestrator.
        /// </summary>
        protected string[] SetDefaultArguments(BuildContext buildContext)
        {
            // The default values are added to the start of command line string.
            // This way, user-provided arguments will be able to override the defaults.
            // If there is a need for a new default argument that's not specific to ADO-runner,
            // it should be added to ConfigurationProvider.GetAdoConfig().
            var defaultArguments = new List<string>() {
                $"/machineHostName:{buildContext.AgentHostName}",
                // By default, set the timeout to 20min in the workers to avoid unnecessary waiting upon connection failures
                "/p:BuildXLWorkerAttachTimeoutMin=20"
            };

            // Enable gRPC encryption
            if (!AdoBuildRunnerService.Config.DisableEncryption)
            {
                defaultArguments.Add("/p:GrpcCertificateSubjectName=CN=1es-hostedpools.default.microsoft.com");
            }

            // By default, enable cache miss analysis and pass the invocation key as a prefix
            var invocationKey = AdoBuildRunnerService.Config.AdoBuildRunnerInvocationKey;
            var cacheMissOption = string.IsNullOrEmpty(invocationKey) ? "/cacheMiss+" : $"/cacheMiss:{invocationKey}";

            defaultArguments.Add(cacheMissOption);

            return defaultArguments.ToArray();
        }
    }
}
