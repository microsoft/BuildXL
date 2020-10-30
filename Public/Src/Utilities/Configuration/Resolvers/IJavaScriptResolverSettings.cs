// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Base settings for all JavaScript-like resolvers
    /// </summary>
    public interface IJavaScriptResolverSettings : IProjectGraphResolverSettings
    {
        /// <summary>
        /// The path to node.exe to use for discovering the JavaScript graph
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
        /// A collection of script commands to execute, where depedencies on other commands can be explicitly provided
        /// E.g. {command: "test", dependsOn: {kind: "local", command: "build"}}
        /// makes the 'test' script depend on the 'build' script of the same project.
        /// Dependencies on other commands of direct dependencies can be specified as well.For example:
        /// {command: "localize", dependsOn: {kind: "project", command: "build"}} makes the 'localize' script depend on 
        /// the 'build' script of all of the project declared dependencies
        /// </summary>
        IReadOnlyList<DiscriminatingUnion<string, IJavaScriptCommand>> Execute { get; }

        /// <summary>
        /// Defines a collection of custom JavaScript commands that can later be used as part of 'execute'.
        /// Allows to extend existing scripts with customized arguments
        /// </summary>
        IReadOnlyList<IExtraArgumentsJavaScript> CustomCommands { get; }

        /// <summary>
        /// Instructs the resolver to expose a collection of exported symbols that other resolvers can consume.
        /// </summary>
        /// <remarks>
        /// Each exported value will have type SharedOpaqueDirectory[], containing the output directories of the specified projects.
        /// </remarks>
        IReadOnlyList<IJavaScriptExport> Exports { get; }

        /// <summary>
        /// When set, the execution of a script command is considered to have failed if the command writes to standard error, regardless of the script command exit code.
        /// </summary>
        /// <remarks>
        /// Defaults to false.
        /// </remarks>
        bool? WritingToStandardErrorFailsExecution { get; }

        /// <summary>
        /// When set, writes under each project node_modules folder is blocked.
        /// </summary>
        /// <remarks>
        /// Defaults to false.
        /// </remarks>
        bool? BlockWritesUnderNodeModules { get; }

        /// <summary>
        /// Policy to apply when a double write occurs.
        /// </summary>
        /// <remarks>
        /// By default double writes are only allowed if the produced content is the same.
        /// </remarks>
        RewritePolicy? DoubleWritePolicy { get; }
    }

    /// <nodoc/>
    public static class IJavaScriptResolverSettingsExtensions
    {
        /// <nodoc/>
        public static string GetCommandName(this DiscriminatingUnion<string, IJavaScriptCommand> command)
        {
            if (command.GetValue() is string simpleCommand)
            {
                return simpleCommand;
            }

            return ((IJavaScriptCommand)command.GetValue()).Command;
        }
    }
}
