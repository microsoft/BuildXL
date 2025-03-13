// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using BuildToolsInstaller.Utilities;

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

            var rootCommand = new RootCommand("Build tools installer");
            
            // Configure root command
            var toolOption = new Option<BuildTool>(
                name: "--tool",
                description: "The tool to install.")
                { IsRequired = true };

            var ringOption = new Option<string?>(
                name: "--ring",
                description: "Selects a deployment ring for the tool");

            var toolsDirectoryOption = new Option<string?>(
                name: "--toolsDirectory",
                description: "The location where packages should be downloaded. Defaults to AGENT_TOOLSDIRECTORY if defined, or the working directory if not");

            var feedOverrideOption = new Option<string?>(
                name: "--feedOverride",
                description: "Uses this Nuget feed as the default upstream");

            var ignoreCacheOption = new Option<bool>(
                name: "--ignoreCache",
                description: "Forces download and installation (prevents tool caching being applied)");

            var toolConfigOption = new Option<string?>(
                 name: "--config",
                 description: "Specific tool installer configuration file.",
                 parseArgument: result =>
                 {
                     if (result.Tokens.Count() != 1)
                     {
                         result.ErrorMessage = "--toolsDirectory should be specified once";
                         return null;
                     }

                     var filePath = result.Tokens.Single().Value;
                     if (!File.Exists(filePath))
                     {
                         result.ErrorMessage = $"The specified config file path '{filePath}' does not exist";
                         return null;
                     }

                     return filePath;
                 });

            rootCommand.AddOption(toolOption);
            rootCommand.AddOption(ringOption);
            rootCommand.AddOption(toolsDirectoryOption);
            rootCommand.AddOption(toolConfigOption);
            rootCommand.AddOption(ignoreCacheOption);

            int returnCode = ProgramNotRunExitCode; // Make the compiler happy, we should assign every time
            rootCommand.SetHandler(async (tool, ring, toolsDirectory, configFile, ignoreCache) =>
            {
                toolsDirectory ??= AdoService.Instance.IsEnabled ? AdoService.Instance.ToolsDirectory : ".";
                returnCode = await BuildToolsInstaller.Run(new SingleToolInstallerArgs()
                {
                    Tool = tool,
                    VersionDescriptor = ring,
                    ToolsDirectory = toolsDirectory,
                    ConfigFilePath = configFile,
                    IgnoreCache = ignoreCache
                });
            },
                toolOption,
                ringOption,
                toolsDirectoryOption,
                toolConfigOption,
                ignoreCacheOption
            );

            // Configure parallel download subcommand
            // TODO [maly]: This should probably be the default and 'single tool' a subcommand
            // but I'll do this while we transition
            var installSubCommand = new Command("install", "Install a set of tools given by a configuration file, in parallel");

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
                description: "Path to the directory containing the global configuration files. If absent, the latest configuraiton package is downloaded from NuGet. This override is provided for local testing and configuration validation.",
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


            installSubCommand.AddOption(configOption);
            installSubCommand.AddOption(globalConfigOption);
            installSubCommand.AddOption(toolsDirectoryOption);
            installSubCommand.AddOption(feedOverrideOption);

            installSubCommand.SetHandler(async (toolsDirectory, toolsConfig, globalConfig, feedOverride) =>
            {
                toolsDirectory ??= AdoService.Instance.IsEnabled ? AdoService.Instance.ToolsDirectory : "1es-tools";

                returnCode = await BuildToolsInstaller.Run(new InstallerArgs()
                {
                    GlobalConfigLocation = globalConfig,
                    ToolsConfigFile = toolsConfig,
                    ToolsDirectory = toolsDirectory,
                    FeedOverride = feedOverride
                });
            },
            toolsDirectoryOption,
            configOption,
            feedOverrideOption,
            globalConfigOption);

            rootCommand.AddCommand(installSubCommand);

            await rootCommand.InvokeAsync(arguments);
            return returnCode;
        }
    }
}
