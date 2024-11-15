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
            if (Environment.GetEnvironmentVariable("BuildToolsDownloaderDebugOnStart") == "1")
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

            var forceOption = new Option<bool>(
                name: "--force",
                description: "Forces download and installation (prevents tool caching being applied)");

            var configOption = new Option<string?>(
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
            rootCommand.AddOption(configOption);
            rootCommand.AddOption(forceOption);

            int returnCode = ProgramNotRunExitCode; // Make the compiler happy, we should assign every time
            rootCommand.SetHandler(async (tool, ring, toolsDirectory, configFile, forceInstallation) =>
            {
                toolsDirectory ??= AdoService.Instance.IsEnabled ? AdoService.Instance.ToolsDirectory : ".";
                returnCode = await BuildToolsInstaller.Run(new BuildToolsInstallerArgs()
                {
                    Tool = tool,
                    Ring = ring,
                    ToolsDirectory = toolsDirectory,
                    ConfigFilePath = configFile,
                    ForceInstallation = forceInstallation
                });
            },
                toolOption,
                ringOption,
                toolsDirectoryOption,
                configOption,
                forceOption
            );

            // Configure config cop subcommand
            var configCopSubCommand = new Command("configcop", "Configuration validation");
            var pathOption = new Option<string>(
                name: "--path",
                description: "The path to the configuration to validate.")
                { IsRequired = true };

            configCopSubCommand.AddOption(pathOption);
            configCopSubCommand.SetHandler<string>(async path => 
            {
                returnCode = await ConfigCop.ValidateConfiguration(path); 
            }, pathOption);


            rootCommand.AddCommand(configCopSubCommand);

            await rootCommand.InvokeAsync(arguments);
            return returnCode;
        }
    }
}
