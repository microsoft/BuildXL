// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BuildXL.Orchestrator.Vsts;

namespace BuildXL.Orchestrator.Build
{
    /// <summary>
    /// A build executor that can execute a build engine depending on agent status and build arguments
    /// </summary>
    public class BuildExecutor : BuildExecutorBase, IBuildExecutor
    {
        /// <nodoc />
        public BuildExecutor(ILogger logger) : base(logger) { }

        private int ExecuteBuild(string executableName, string arguments, string buildSourcesDirectory)
        {
            var process = new Process()
            {
                StartInfo =
                {
                    FileName = executableName,
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

            Logger.Info($"Launching: {process.StartInfo.FileName} {process.StartInfo.Arguments}");

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

        private static string ExtractAndEscapeCommandLineArguments(string[] exec)
        {
            var args = exec.Skip(1);
            return string.Join(" ", args);
        }

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
            Logger.Info($@"Launching single machine build!");
            return ExecuteBuild(buildArguments[0], ExtractAndEscapeCommandLineArguments(buildArguments), buildContext.SourcesDirectory);
        }

        /// <inherit />
        public int ExecuteDistributedBuildAsMaster(BuildContext buildContext, string[] buildArguments, List<IDictionary<string, string>> machines)
        {
            Logger.Info($@"Launching distributed build as master!");

            var workers = machines.Select(d => $"/distributedBuildWorker:{d[Constants.MachineIpV4Address]}:{Constants.MachineGrpcPort}");
            var workerFlags = string.Join(" ", workers);

            return ExecuteBuild(
                buildArguments[0],
                ExtractAndEscapeCommandLineArguments(buildArguments) +
                $" /distributedBuildRole:master /distributedBuildServicePort:{Constants.MachineGrpcPort} " +
                workerFlags,
                buildContext.SourcesDirectory
            );
        }

        /// <inherit />
        public int ExecuteDistributedBuildAsWorker(BuildContext buildContext, string[] buildArguments, IDictionary<string, string> masterInfo)
        {
            Logger.Info($@"Launching distributed build as worker!");

            return ExecuteBuild(
                buildArguments[0],
                ExtractAndEscapeCommandLineArguments(buildArguments) +
                $" /distributedBuildRole:worker /distributedBuildServicePort:{Constants.MachineGrpcPort}",
                buildContext.SourcesDirectory
            );
        }
    }
}
