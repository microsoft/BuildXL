// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.FrontEnd.JavaScript.ProjectGraph
{
    /// <summary>
    /// Useful for deserializing a project graph where dependencies are specified as strings, 
    /// </summary>
    /// <remarks>
    /// There is a 1:1 mapping between a <see cref="DeserializedJavaScriptProject"/> and and a package.json. All specified
    /// script commands are available since the project-to-project graph is agnostic to the requested script commands.
    /// </remarks>
    [DebuggerDisplay("{Name}")]
    public sealed class DeserializedJavaScriptProject : GenericJavaScriptProject<string>
    {
        /// <nodoc/>
        public DeserializedJavaScriptProject(
            string name,
            AbsolutePath projectFolder,
            IReadOnlyCollection<string> dependencies,
            [AllowNull] IReadOnlyDictionary<string, string> availableScriptCommands,
            AbsolutePath tempFolder,
            IReadOnlyCollection<PathWithTargets> outputDirectories,
            IReadOnlyCollection<PathWithTargets> sourceFiles,
            IReadOnlyCollection<AbsolutePath> sourceDirectories,
            bool cacheable,
            int timeoutInMilliseconds = 0,
            int warningTimeoutInMilliseconds = 0) : base(name, projectFolder, dependencies, tempFolder, cacheable, timeoutInMilliseconds, warningTimeoutInMilliseconds)
        {
            Contract.RequiresNotNull(dependencies);
            Contract.RequiresNotNull(outputDirectories);
            Contract.RequiresNotNull(sourceFiles);

            AvailableScriptCommands = availableScriptCommands ?? CollectionUtilities.EmptyDictionary<string, string>();
            OutputDirectories = outputDirectories;
            SourceFiles = sourceFiles;
            SourceDirectories = sourceDirectories ?? CollectionUtilities.EmptyArray<AbsolutePath>();
        }

        /// <nodoc/>
        public DeserializedJavaScriptProject WithCustomScripts(IReadOnlyDictionary<string, string> customScriptCommands)
        {
            Contract.Requires(customScriptCommands != null);

            return new DeserializedJavaScriptProject(
                Name, ProjectFolder, Dependencies, customScriptCommands, TempFolder, OutputDirectories, SourceFiles, SourceDirectories, Cacheable, TimeoutInMilliseconds, WarningTimeoutInMilliseconds);
        }

        /// <summary>
        /// The script commands that are available for the project
        /// </summary>
        public IReadOnlyDictionary<string, string> AvailableScriptCommands { get; }
        
        /// <nodoc/>
        public IReadOnlyCollection<PathWithTargets> OutputDirectories { get; }

        /// <nodoc/>
        public IReadOnlyCollection<PathWithTargets> SourceFiles { get; }

        /// <summary>
        /// Directory scopes the project is allowed to read from.
        /// </summary>
        /// <remarks>
        /// Only honored when EnforceSourceReadsUnderPackageRoots knob in the resolver settings is enabled.
        /// </remarks>
        public IReadOnlyCollection<AbsolutePath> SourceDirectories { get; }
    }
}
