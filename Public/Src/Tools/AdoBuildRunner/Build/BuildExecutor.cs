// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BuildXL.AdoBuildRunner.Vsts;

#nullable enable

namespace BuildXL.AdoBuildRunner.Build
{
    /// <summary>
    /// A build executor that can execute a build engine depending on agent status and build arguments
    /// </summary>
    public class BuildExecutor : BuildExecutorBase, IBuildExecutor
    {
        private readonly string m_bxlExeLocation;

        /// <nodoc />
        public BuildExecutor(ILogger logger) : base(logger) 
        {
            // Resolve the bxl executable location
            var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "bxl" : "bxl.exe";
            m_bxlExeLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, exeName);
        }

        private int ExecuteBuild(string arguments, string buildSourcesDirectory)
        {
            var process = new Process()
            {
                StartInfo =
                {
                    FileName = m_bxlExeLocation,
                    Arguments = arguments,
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

        private void SetEnvVars(BuildContext buildContext)
        {
            // Extend this eventually, with the context needed for the builds
        }

        private static string ExtractAndEscapeCommandLineArguments(string[] args) => string.Join(" ", args);

        /// <inherit />
        public void PrepareBuildEnvironment(BuildContext buildContext)
        {
            if (buildContext == null)
            {
                throw new ArgumentNullException(nameof(buildContext));
            }

            SetEnvVars(buildContext);
        }

        /// <inherit />
        public int ExecuteSingleMachineBuild(BuildContext buildContext, string[] buildArguments)
        {
            Logger.Info($@"Launching single machine build");
            return ExecuteBuild(ExtractAndEscapeCommandLineArguments(buildArguments), buildContext.SourcesDirectory);
        }

        /// <inherit />
        public int ExecuteDistributedBuildAsOrchestrator(BuildContext buildContext, string[] buildArguments)
        {
            Logger.Info($@"Launching distributed build as orchestrator");

            return ExecuteBuild(
                ExtractAndEscapeCommandLineArguments(buildArguments) +
                $" /ado /distributedBuildRole:master" +
                $" /distributedBuildServicePort:{Constants.MachineGrpcPort}" +
                $" /relatedActivityId:{buildContext.RelatedSessionId}",
                buildContext.SourcesDirectory
            );
        }

        /// <inherit />
        public int ExecuteDistributedBuildAsWorker(BuildContext buildContext, string[] buildArguments)
        {
            Logger.Info($@"Launching distributed build as worker");

            return ExecuteBuild(
                "/p:BuildXLWorkerAttachTimeoutMin=20 " +  // By default, set the timeout to 20min in the workers to avoid unnecessary waiting upon connection failures
                ExtractAndEscapeCommandLineArguments(buildArguments) +
                $" /ado /distributedBuildRole:worker" +
                $" /distributedBuildServicePort:{Constants.MachineGrpcPort}" +
                $" /distributedBuildOrchestratorLocation:{buildContext.OrchestratorLocation}" +
                $" /relatedActivityId:{buildContext.RelatedSessionId}",
                buildContext.SourcesDirectory
            );
        }

        /// <inheritdoc />
        public void InitializeAsWorker(BuildContext buildContext, string[] buildArguments)
        {
            // No prep work to do
        }
    }
}
