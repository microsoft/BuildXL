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
            // We want these argument in all builds run by the AdoBuildRunner
            arguments += " /ado";
            
            // Enable gRPC encryption
            if (!(Environment.GetEnvironmentVariable(Constants.DisableEncryptionVariableName) == "1"))
            {
                arguments += " /p:GrpcCertificateSubjectName=CN=1es-hostedpools.default.microsoft.com /p:GrpcCertificateStoreLocation=CurrentUser";
            }

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
        public int ExecuteDistributedBuildAsOrchestrator(BuildContext buildContext, string relatedSessionId, string[] buildArguments)
        {
            Logger.Info($@"Launching distributed build as orchestrator");
            return ExecuteBuild(
                ExtractAndEscapeCommandLineArguments(buildArguments) +
                $" /distributedBuildRole:orchestrator" +
                $" /distributedBuildServicePort:{Constants.MachineGrpcPort}" +
                $" /relatedActivityId:{relatedSessionId}",
                buildContext.SourcesDirectory
            );
        }

        /// <inherit />
        public int ExecuteDistributedBuildAsWorker(BuildContext buildContext, BuildInfo buildInfo, string[] buildArguments)
        {
            Logger.Info($@"Launching distributed build as worker");

            return ExecuteBuild(
                "/p:BuildXLWorkerAttachTimeoutMin=20 " +  // By default, set the timeout to 20min in the workers to avoid unnecessary waiting upon connection failures
                ExtractAndEscapeCommandLineArguments(buildArguments) +
                $" /distributedBuildRole:worker" +
                $" /distributedBuildServicePort:{Constants.MachineGrpcPort}" +
                $" /distributedBuildOrchestratorLocation:{buildInfo.OrchestratorLocation}:{Constants.MachineGrpcPort}" +
                $" /relatedActivityId:{buildInfo.RelatedSessionId}",
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
