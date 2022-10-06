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
        /// Alternatively to a path, a collection of directories to seach for node.exe can be provided.
        /// If not provided, node.exe will be looked in PATH
        /// </remarks>
        DiscriminatingUnion<FileArtifact, IReadOnlyList<DirectoryArtifact>> NodeExeLocation { get; }

        /// <summary>
        /// Extra dependencies that can be specified for selected projects. 
        /// </summary>
        /// <remarks>
        /// Dependencies can be declared against JavaScript projects or regular files or directories.
        /// These additional dependencies are added to the build graph after the regular project-to-project are computed</remarks>
        IReadOnlyList<IJavaScriptDependency> AdditionalDependencies { get; }

        /// <summary>
        /// A collection of script commands to execute, where depedencies on other commands can be explicitly provided
        /// E.g. {command: "test", dependsOn: {kind: "local", command: "build"}}
        /// makes the 'test' script depend on the 'build' script of the same project.
        /// Dependencies on other commands of direct dependencies can be specified as well.For example:
        /// {command: "localize", dependsOn: {kind: "project", command: "build"}} makes the 'localize' script depend on 
        /// the 'build' script of all of the project declared dependencies
        /// </summary>
        IReadOnlyList<DiscriminatingUnion<string, IJavaScriptCommand, IJavaScriptCommandGroupWithDependencies, IJavaScriptCommandGroup>> Execute { get; }

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

        /// <summary>
        /// When specified, the resolver will give this callback an opportunity to schedule pips based on each project information. 
        /// </summary>
        /// <remarks>
        /// The callback will be executed for every project discovered by this resolver. When the callback is present, the resolver won't schedule the given 
        /// project and the callback is responsible for doing it.
        /// The callback defines the location a function whose expected type is (JavaScriptProject) => TransformerExecuteResult.The
        /// resolver will create an instance of an JavaScriptProject for each discovered project and pass it along.
        /// The callback can decide not to schedule a given project by returning 'undefined', in which case the resolver will schedule it in the
        /// regular way
        /// </remarks>
        ICustomSchedulingCallback CustomScheduling { get; }

        /// <summary>
        /// Callback specifying custom scripts
        /// </summary>
        /// <remarks>
        /// The object is a closure, enforced by the DScript type checker. The Closure type is defined in the TypeScript DLL, not easily accessible from here.
        /// </remarks>
        object CustomScripts { get; }

        /// <summary>
        /// A custom set of success exit codes that applies to all pips scheduled by this resolver. 
        /// </summary>
        /// <remarks>
        /// Any other exit code would indicate failure. If unspecified, by default, 0 is the only successful exit code. 
        /// </remarks>
        IReadOnlyList<int> SuccessExitCodes { get; }

        /// <summary>
        /// A custom set of exit codes that causes pips to be retried by BuildXL. 
        /// </summary>
        /// <remarks>
        /// Applies to all pips scheduled by this resolver. 
        /// If an exit code is also in the successExitCode, then the pip is not retried on exiting with that exit code.
        /// </remarks>
        IReadOnlyList<int> RetryExitCodes { get; }

        /// <summary>
        /// Maximum number of retries for processes.
        /// </summary>
        /// <remarks>
        /// Applies to all processes scheduled by this resolver.
        /// A process returning an exit code specified in 'retryExitCodes' will be retried at most the specified number of times.
        /// </remarks>
        int? ProcessRetries { get; }

        /// <summary>
        /// The timeout in milliseconds that the execution sandbox waits for child processes started by the top-level process to exit after the top-level process exits.
        /// </summary>
        int? NestedProcessTerminationTimeoutMs { get; }

        /// <summary>
        /// Debug option to enable sandbox logging. Corresponding logs will be sent to the project-level log folder.
        /// </summary>
        /// <remarks>
        /// Enabling this option may affect performance. Intended for debugging only.
        /// </remarks>
        bool? EnableSandboxLogging { get; }
    }

    /// <nodoc/>
    public static class IJavaScriptResolverSettingsExtensions
    {
        /// <nodoc/>
        public static string GetCommandName(this DiscriminatingUnion<string, IJavaScriptCommand, IJavaScriptCommandGroupWithDependencies, IJavaScriptCommandGroup> command)
        {
            object value = command.GetValue();
            if (value is string simpleCommand)
            {
                return simpleCommand;
            }
            else if (value is IJavaScriptCommand)
            {
                return ((IJavaScriptCommand)command.GetValue()).Command;
            }

            return ((IJavaScriptCommandGroup)command.GetValue()).CommandName;
        }
    }
}
