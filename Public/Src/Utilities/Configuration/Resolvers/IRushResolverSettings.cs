// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Rush resolver
    /// </summary>
    public interface IRushResolverSettings : IProjectGraphResolverSettings
    {
        /// <summary>
        /// The base directory location to look for @microsoft/rush-lib module, used to build the project graph
        /// </summary>
        /// <remarks>
        /// If not provided, BuildXL will try to find it based on Rush installation location
        /// </remarks>
        DirectoryArtifact? RushLibBaseLocation { get; }

        /// <summary>
        /// The path to node.exe to use for discovering the Rush graph
        /// </summary>
        /// <remarks>
        /// If not provided, node.exe will be looked in PATH
        /// </remarks>
        FileArtifact? NodeExeLocation { get; }

        /// <summary>
        /// Collection of additional output directories pips may write to
        /// </summary>
        /// <remarks>
        /// If a relative path is provided, it will be interpreted relative to every project root
        /// </remarks>
        IReadOnlyList<DiscriminatingUnion<AbsolutePath, RelativePath>> AdditionalOutputDirectories { get; }
        
        /// <summary>
        /// A collection of Rush command to execute, where depedencies on other commands can be explicitly provided
        /// E.g. {command: "test", dependsOn: {kind: "local", command: "build"}}
        /// makes the 'test' script depend on the 'build' script of the same project.
        /// Dependencies on other commands of direct dependencies can be specified as well.For example:
        /// {command: "localize", dependsOn: {kind: "project", command: "build"}} makes the 'localize' script depend on 
        /// the 'build' script of all of the project declared dependencies
        /// </summary>
        IReadOnlyList<DiscriminatingUnion<string, IRushCommand>> Execute { get; }

        /// <summary>
        /// Defines a collection of custom Rush commands that can later be used as part of 'execute'.
        /// Allows to extend existing scripts with customized arguments
        /// </summary>
        IReadOnlyList<IExtraArgumentsRushScript> CustomCommands { get; }
    }

    /// <nodoc/>
    public static class IRushResolverSettingsExtensions
    {
        /// <nodoc/>
        public static string GetCommandName(this DiscriminatingUnion<string, IRushCommand> command)
        {
            if (command.GetValue() is string simpleCommand)
            {
                return simpleCommand;
            }

            return ((IRushCommand)command.GetValue()).Command;
        }
    }
}
