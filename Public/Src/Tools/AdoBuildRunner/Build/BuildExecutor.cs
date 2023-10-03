// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        private int ExecuteBuild(IEnumerable<string> arguments, string buildSourcesDirectory)
        {
            var fullArguments = SetDefaultArguments(arguments);

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

        private void SetEnvVars(BuildContext buildContext)
        {
            // Extend this eventually, with the context needed for the builds
        }

        private static string ExtractAndEscapeCommandLineArguments(IEnumerable<string> args) => string.Join(' ', args);

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
            return ExecuteBuild(buildArguments, buildContext.SourcesDirectory);
        }

        /// <inherit />
        public int ExecuteDistributedBuildAsOrchestrator(BuildContext buildContext, string relatedSessionId, string[] buildArguments)
        {
            Logger.Info($@"Launching distributed build as orchestrator");
            return ExecuteBuild(
                buildArguments.Concat(new[]
                {
                    $"/distributedBuildRole:orchestrator",
                    $"/distributedBuildServicePort:{Constants.MachineGrpcPort}",
                    $"/relatedActivityId:{relatedSessionId}"
                }),
                buildContext.SourcesDirectory
            );
        }

        /// <inherit />
        public int ExecuteDistributedBuildAsWorker(BuildContext buildContext, BuildInfo buildInfo, string[] buildArguments)
        {
            Logger.Info($@"Launching distributed build as worker");

            return ExecuteBuild(
                // By default, set the timeout to 20min in the workers to avoid unnecessary waiting upon connection failures
                // (defaults are placed in front of user-provided arguments).
                new[]
                {
                    "/p:BuildXLWorkerAttachTimeoutMin=20"
                }
                .Concat(buildArguments)
                .Concat(new[]
                {
                    $"/distributedBuildRole:worker",
                    $"/distributedBuildServicePort:{Constants.MachineGrpcPort}",
                    $"/distributedBuildOrchestratorLocation:{buildInfo.OrchestratorLocation}:{Constants.MachineGrpcPort}",
                    $"/relatedActivityId:{buildInfo.RelatedSessionId}"
                }),
                buildContext.SourcesDirectory
            );
        }

        /// <inheritdoc />
        public void InitializeAsWorker(BuildContext buildContext, string[] buildArguments)
        {
            // No prep work to do
        }

        private static IEnumerable<string> SetDefaultArguments(IEnumerable<string> arguments)
        {
            // The default values are added to the start of command line string.
            // This way, user-provided arguments will be able to override the defaults.
            var defaultArguments = new List<string>() {
                // Out default pip timeout is 10 min; increase it to 30min
                "/pipTimeoutMultiplier:3",
                // We are indeed running in ADO
                "/ado",
                // Setting this high enough, so the scheduler pausing is only based on MaximumRamUtilizationPercentage.
                // We check that both conditions (min ram and max utilization %) are met, before pausing the scheduler.
                "/minAvailableRamMb:100000000",
                // Both historical perf info and the early release are not working well in ADO environment,
                // so temporarily disable them. This can be removed once the features are fixed (TODO #2106086)
                "/useHistoricalRamUsageInfo-",
                "/earlyWorkerRelease-",
                // This flag could make sense to enable as default across the board (not only for ADO), but for now
                // let's keep dev builds out of it until we can validate it doesn't introduce a regression.
                "/useHistoricalCpuUsageInfo+"
            };

            // Enable gRPC encryption
            if (!(Environment.GetEnvironmentVariable(Constants.DisableEncryptionVariableName) == "1"))
            {
                defaultArguments.Add("/p:GrpcCertificateSubjectName=CN=1es-hostedpools.default.microsoft.com /p:GrpcCertificateStoreLocation=CurrentUser");
            }

            // add specified arguments
            defaultArguments.AddRange(arguments);

            return defaultArguments;
        }
    }
}
