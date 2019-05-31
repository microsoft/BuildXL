// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net;
using BuildXL.App.Tracing;
using BuildXL.Native.IO.Windows;
using BuildXL.Native.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Strings = bxl.Strings;

namespace BuildXL
{
    /// <summary>
    /// bxl.exe entry point. Sometimes a new process runs actual build work; other times it is a client for an existing 'app server'.
    /// <see cref="BuildXLApp"/> is the 'app' itself which does actual work. <see cref="AppServer"/> is named pipe-accessible host for that app.
    /// An app server is effectively a cached
    /// instance of bxl.exe to amortize some startup overheads, whereas a 'listen mode' build engine (possibly running inside an app server) is a
    /// remotely controlled build (single graph, defined start and end point).
    /// </summary>
    internal sealed class Program
    {
        internal const string BuildXlAppServerConfigVariable = "BUILDXL_APP_SERVER_CONFIG";

        [STAThread]
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        public static int Main(string[] rawArgs)
        {
            // TODO:#1208464 - this can be removed once BuildXL targets .net or newer 4.7 where TLS 1.2 is enabled by default
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;

            Program p = new Program(rawArgs);

            // Note that we do not wrap Run in a catch-all exception handler. If we did, then last-chance handling (i.e., an 'unhandled exception'
            // event) is neutered - but only for the main thread! Instead, we want to have a uniform last-chance handling method that does the
            // right telemetry / Windows Error Reporting magic as part of crashing (possibly into a debugger).
            // TODO: Promote the last-chance handler from BuildXLApp to here?
            return p.Run();
        }

        private string[] RawArgs { get; set; }

        private Program(string[] rawArgs)
        {
            RawArgs = rawArgs;
        }

        /// <summary>
        /// The core execution of the tool.
        /// </summary>
        /// <remarks>
        /// If you discover boilerplate in multiple implementations, add it to MainImpl, or add another inheritance hierarchy.
        /// </remarks>
        public int Run()
        {
            // We may have been started to be an app server. See StartAppServerProcess. If so, run as an app server (and expect no args).
            string startupParamsSerialized = Environment.GetEnvironmentVariable(BuildXlAppServerConfigVariable);
            if (startupParamsSerialized != null)
            {
                if (RawArgs.Length > 0)
                {
                    // TODO: Message
                    return ExitCode.FromExitKind(ExitKind.InvalidCommandLine);
                }

                AppServer.StartupParameters startupParameters = AppServer.StartupParameters.TryParse(startupParamsSerialized);
                if (startupParameters == null)
                {
                    return ExitCode.FromExitKind(ExitKind.InvalidCommandLine);
                }

                return ExitCode.FromExitKind(RunAppServer(startupParameters));
            }

            LightConfig lightConfig;

            if (!LightConfig.TryParse(RawArgs, out lightConfig) && lightConfig.Help == HelpLevel.None)
            {
                // If light config parsing failed, go through the full argument parser to collect & print the errors
                // it would catch.
                ICommandLineConfiguration config;
                Analysis.IgnoreResult(Args.TryParseArguments(RawArgs, new PathTable(), null, out config));
                HelpText.DisplayHelp(BuildXL.ToolSupport.HelpLevel.Verbose);
                return ExitCode.FromExitKind(ExitKind.InvalidCommandLine);
            }

            // Not an app server; will either run fully within this process ('single instance') or start / connect to an app server.
            if (!lightConfig.NoLogo)
            {
                HelpText.DisplayLogo();
            }

            if (lightConfig.Help != HelpLevel.None)
            {
                if (lightConfig.Help == HelpLevel.DxCode)
                {
                    System.Diagnostics.Process.Start(Strings.DX_Help_Link);
                }
                else
                {
                    // Need to cast here to convert from the configuration enum to the ToolSupoort enum. Their values
                    // are manually kept in sync to avoid the additional dependency.
                    HelpText.DisplayHelp((BuildXL.ToolSupport.HelpLevel)lightConfig.Help);
                }

                return ExitCode.FromExitKind(ExitKind.BuildNotRequested);
            }

            // Optionally perform some special tasks related to server mode
            switch (lightConfig.Server)
            {
                case ServerMode.Kill:
                    ServerDeployment.KillServer(ServerDeployment.ComputeDeploymentDir(lightConfig.ServerDeploymentDirectory));
                    Console.WriteLine(Strings.App_ServerKilled);
                    return ExitCode.FromExitKind(ExitKind.BuildNotRequested);
                case ServerMode.Reset:
                    ServerDeployment.PoisonServerDeployment(lightConfig.ServerDeploymentDirectory);
                    break;
            }

            ExitKind exitKind = lightConfig.Server != ServerMode.Disabled
                ? ConnectToAppServerAndRun(lightConfig, RawArgs)
                : RunSingleInstance(RawArgs);

            return ExitCode.FromExitKind(exitKind);
        }

        private static ExitKind RunSingleInstance(IReadOnlyCollection<string> rawArgs, ServerModeStatusAndPerf? serverModeStatusAndPerf = null)
        {
            using (var args = new Args())
            {
                var pathTable = new PathTable();

                ICommandLineConfiguration configuration;
                if (!args.TryParse(rawArgs.ToArray(), pathTable, out configuration))
                {
                    return ExitKind.InvalidCommandLine;
                }

                string clientPath = AssemblyHelper.GetThisProgramExeLocation();
                var rawArgsWithExe = new List<string>(rawArgs.Count + 1) { clientPath };
                rawArgsWithExe.AddRange(rawArgs);

                using (var app = new BuildXLApp(
                    new SingleInstanceHost(),
                    null,

                    // BuildXLApp will create a standard console.
                    configuration,
                    pathTable,
                    rawArgsWithExe,
                    null,
                    serverModeStatusAndPerf))
                {
                    Console.CancelKeyPress +=
                        (sender, eventArgs) =>
                        {
                            eventArgs.Cancel = !app.OnConsoleCancelEvent(isTermination: eventArgs.SpecialKey == ConsoleSpecialKey.ControlBreak);
                        };

                    return app.Run().ExitKind;
                }
            }
        }

        private static ExitKind ConnectToAppServerAndRun(LightConfig lightConfig, IReadOnlyList<string> rawArgs)
        {
            using (AppServer.Connection connection = AppServer.TryStartOrConnect(
                startupParameters => TryStartAppServerProcess(startupParameters),
                lightConfig,
                lightConfig.ServerDeploymentDirectory,
                out var serverModeStatusAndPerf,
                out var environmentVariablesToPass))
            {
                if (connection == null)
                {
                    // Connection failed; fall back to single instance.
                    return RunSingleInstance(rawArgs, serverModeStatusAndPerf);
                }
                else
                {
                    try
                    {
                        return connection.RunWithArgs(rawArgs, environmentVariablesToPass, serverModeStatusAndPerf);
                    }
                    catch (BuildXLException ex)
                    {
                        Console.Error.WriteLine(Strings.AppServer_TerminatingClient_PipeDisconnect, ex.Message);
                        return ExitKind.InternalError;
                    }
                }
            }
        }

        private static ExitKind RunAppServer(AppServer.StartupParameters startupParameters)
        {
            ExitKind exitKind;
            using (
                var server =
                    new AppServer(maximumIdleTime: new TimeSpan(hours: 0, minutes: startupParameters.ServerMaxIdleTimeInMinutes, seconds: 0)))
            {
                exitKind = server.Run(startupParameters);
            }

            Exception telemetryShutdownException;
            if (AriaV2StaticState.TryShutDown(out telemetryShutdownException) == AriaV2StaticState.ShutDownResult.Failure)
            {
                exitKind = ExitKind.InfrastructureError;
            }

            return exitKind;
        }

        private static Possible<Unit> TryStartAppServerProcess(AppServer.StartupParameters startupParameters)
        {
            // For simplicity, we clone the current environment block - but add one additional variable.
            // Some variables like PATH are needed for the server to even start. Others like the COMPlus_*
            // family and BuildXLDebugOnStart affect its startup behavior. However, note that each
            // client actually separately provides a replacement environment block to the server; these blocks
            // are intended to affect the pip graph, rather than server startup.
            var environment = new Dictionary<string, string>();
            foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            {
                environment[(string)variable.Key] = (string)variable.Value;
            }

            environment[BuildXlAppServerConfigVariable] = startupParameters.ToString();

            // We create a 'detached' process, since this server process is supposed to live a long time.
            // - We don't inherit any handles (maybe the caller accidentally leaked a bunch of pipe handles into this client process).
            // - We allocate a new hidden console.
            // - We require breakaway from containing jobs (otherwise, someone might use it to wait on the server by accident).
            int newProcessId;
            int errorCode;
            var status = BuildXL.Native.Processes.ProcessUtilities.CreateDetachedProcess(
                commandLine: startupParameters.PathToProcess,
                environmentVariables: environment,

                // Explicitly use the directory of the server process as the working directory. This will later be reset
                // to whatever directory the client is in when it connects to the server process. Some directory is needed
                // here, but it is intentionally using the incorrect directory rather than something that looks correct
                // since this is a once-only set. The correct path needs to be set for each build.
                workingDirectory: Path.GetDirectoryName(startupParameters.PathToProcess),
                newProcessId: out newProcessId,
                errorCode: out errorCode);

            switch (status)
            {
                case CreateDetachedProcessStatus.Succeeded:
                    return Unit.Void;
                case CreateDetachedProcessStatus.JobBreakwayFailed:
                    return new Failure<string>("The server process could not break-away from a containing job");
                case CreateDetachedProcessStatus.ProcessCreationFailed:
                    return new NativeFailure(errorCode).Annotate("Failed to create a server process");
                default:
                    throw Contract.AssertFailure("Unhandled CreateDetachedProcessStatus");
            }
        }
    }

    /// <summary>
    /// Provides access to the entry point of the assembly without taking a dependency on
    /// OneBuildProgram and associated types
    /// </summary>
    internal static class EntryPoint
    {
        /// <summary>
        /// Calls entrypoint of the assembly
        /// </summary>
        public static int Run(string[] args)
        {
            return Program.Main(args);
        }
    }
}
