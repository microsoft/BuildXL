// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Configuration.Resolvers;
using BuildXL.Utilities.Configuration.Resolvers.Mutable;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Resolver settings for a JavaScript-based front-end.
    /// </summary>
    public class JavaScriptResolverSettings : UntrackingResolverSettings, IJavaScriptResolverSettings
    {
        /// <nodoc/>
        public JavaScriptResolverSettings()
        {
            // We allow the source directory to be writable by default
            AllowWritableSourceDirectory = true;
            // Request full reparse point resolution for all JS based resolvers
            RequestFullReparsePointResolving = true;
        }

        /// <nodoc/>
        public JavaScriptResolverSettings(
            IJavaScriptResolverSettings resolverSettings,
            PathRemapper pathRemapper)
            : base(resolverSettings, resolverSettings, pathRemapper)
        {
            Root = pathRemapper.Remap(resolverSettings.Root);
            ModuleName = resolverSettings.ModuleName;
            Environment = resolverSettings.Environment;
            KeepProjectGraphFile = resolverSettings.KeepProjectGraphFile;
            NodeExeLocation = pathRemapper.Remap(resolverSettings.NodeExeLocation);
            AdditionalOutputDirectories = resolverSettings.AdditionalOutputDirectories;
            Execute = resolverSettings.Execute;
            CustomCommands = resolverSettings.CustomCommands;
            Exports = resolverSettings.Exports;
            WritingToStandardErrorFailsExecution = resolverSettings.WritingToStandardErrorFailsExecution;
            DoubleWritePolicy = resolverSettings.DoubleWritePolicy;
            CustomScheduling = resolverSettings.CustomScheduling;
            CustomScripts = resolverSettings.CustomScripts;
            SuccessExitCodes = resolverSettings.SuccessExitCodes;
            RetryExitCodes = resolverSettings.RetryExitCodes;
            ProcessRetries = resolverSettings.ProcessRetries;
            AdditionalDependencies = resolverSettings.AdditionalDependencies?.Select(additionalDependency => new JavaScriptDependency(additionalDependency, pathRemapper))?.ToList();
            NestedProcessTerminationTimeoutMs = resolverSettings.NestedProcessTerminationTimeoutMs;
            EnableSandboxLogging = resolverSettings.EnableSandboxLogging;
        }

        /// <inheritdoc/>
        public AbsolutePath Root { get; set; }

        /// <inheritdoc/>
        public string ModuleName { get; set; }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, EnvironmentData> Environment { get; set; }

        /// <inheritdoc/>
        public bool? KeepProjectGraphFile { get; set; }

        /// <inheritdoc/>
        public DiscriminatingUnion<FileArtifact, IReadOnlyList<DirectoryArtifact>> NodeExeLocation { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<AbsolutePath, RelativePath>> AdditionalOutputDirectories { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptCommand, IJavaScriptCommandGroupWithDependencies, IJavaScriptCommandGroup>> Execute { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IExtraArgumentsJavaScript> CustomCommands { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IJavaScriptExport> Exports { get; set; }

        /// <inheritdoc/>
        public bool? WritingToStandardErrorFailsExecution { get; set;  }

        /// <inheritdoc/>
        public bool? BlockWritesUnderNodeModules { get; set; }

        /// <inheritdoc />
        public RewritePolicy? DoubleWritePolicy { get; set; }

        /// <inheritdoc />
        public ICustomSchedulingCallback CustomScheduling { get; set; }

        /// <inheritdoc />
        public object CustomScripts { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<int> SuccessExitCodes { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<int> RetryExitCodes { get; set; }

        /// <inheritdoc/>
        public int? ProcessRetries { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IJavaScriptDependency> AdditionalDependencies { get; set; }

        /// <inheritdoc/>
        public int? NestedProcessTerminationTimeoutMs { get; set; }

        /// <inheritdoc/>
        public bool? EnableSandboxLogging { get; set; }
    }
}
