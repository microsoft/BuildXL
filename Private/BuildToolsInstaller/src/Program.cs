// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using BuildToolsInstaller.Utilities;
using NuGet.Versioning;

namespace BuildToolsInstaller
{
    internal sealed class Program
    {
        private const int ProgramNotRunExitCode = 21; // Just a random number to please the compiler

        /// <nodoc />
        public static async Task<int> Main(string[] arguments)
        {
            if (Environment.GetEnvironmentVariable("BuildToolsInstallerDebugOnStart") == "1")
            {
                System.Diagnostics.Debugger.Launch();
            }

            int returnCode = ProgramNotRunExitCode; // Make the compiler happy, we should assign every time
            var rootCommand = new RootCommand("Build tools installer");
            var installSubCommand = new Command("install", "Install a set of tools given by a configuration file, in parallel");

            var toolsDirectoryOption = new Option<string?>(
                name: "--toolsDirectory",
                description: "The location where packages should be downloaded. Defaults to AGENT_TOOLSDIRECTORY if defined, or the working directory if not");

            var feedOverrideOption = new Option<string?>(
                name: "--feedOverride",
                description: "Uses this Nuget feed as the default upstream");

            var configOption = new Option<string>(
                name: "--config",
                description: "Path to the JSON file listing the tools to install.",
                parseArgument: result =>
                {
                    if (result.Tokens.Count() != 1)
                    {
                        result.ErrorMessage = "--config should be specified once";
                        return null!;
                    }

                    var filePath = result.Tokens.Single().Value;
                    if (!File.Exists(filePath))
                    {
                        result.ErrorMessage = $"The specified file path '{filePath}' does not exist";
                        return string.Empty;
                    }

                    return filePath;
                })
            { IsRequired = true };

            var globalConfigOption = new Option<string>(
                name: "--globalConfigDirectory",
                description: "Path to the directory containing the global configuration files. If absent, the latest configuration package is downloaded from NuGet. This override is provided for local testing and configuration validation.",
                parseArgument: result =>
                {
                    if (result.Tokens.Count() != 1)
                    {
                        result.ErrorMessage = "--globalConfigDirectory should be specified once";
                        return null!;
                    }

                    var path = result.Tokens.Single().Value;
                    if (!Directory.Exists(path))
                    {
                        result.ErrorMessage = $"The specified file path '{path}' does not exist";
                        return string.Empty;
                    }

                    return path;
                })
            { IsRequired = false };

            var configVersionOption = new Option<NuGetVersion?>(
                name: "--configVersion",
                description: "Specify a version of the configuration package to download instead of using the latest",
                parseArgument: result =>
                {
                    var version = result.Tokens.LastOrDefault()?.Value;
                    if (!NuGetVersion.TryParse(version, out var nugetVersion))
                    {
                        result.ErrorMessage = $"Could not parse the NuGet version '{version}'";
                        return default;
                    }

                    return nugetVersion;
                })
            { IsRequired = false };

            var workerModeOption = new Option<bool>(
                name: "--workerMode",
                description: "If set, the installer will run in worker mode. This is only valid when running in ADO builds. In this mode, the installer will wait for an installer in a corresponding orchestrator job to provide the version of the configuration to use.");

            installSubCommand.AddOption(configOption);
            installSubCommand.AddOption(globalConfigOption);
            installSubCommand.AddOption(toolsDirectoryOption);
            installSubCommand.AddOption(feedOverrideOption);
            installSubCommand.AddOption(configVersionOption);
            installSubCommand.AddOption(workerModeOption);

            installSubCommand.SetHandler(async (toolsDirectory, toolsConfig, globalConfig, feedOverride, configVersion, workerMode) =>
            {
                toolsDirectory ??= AdoService.Instance.IsEnabled ? AdoService.Instance.ToolsDirectory : "1es-tools";

                returnCode = await BuildToolsInstaller.Run(new InstallerArgs()
                {
                    GlobalConfigLocation = globalConfig,
                    ToolsConfigFile = toolsConfig,
                    ToolsDirectory = toolsDirectory,
                    FeedOverride = feedOverride,
                    ConfigVersion = configVersion,
                    WorkerMode = workerMode
                });
            },
            toolsDirectoryOption,
            configOption,
            globalConfigOption,
            feedOverrideOption,
            configVersionOption,
            workerModeOption);

            rootCommand.AddCommand(installSubCommand);

            await rootCommand.InvokeAsync(arguments);
            return returnCode;
        }
    }
}
