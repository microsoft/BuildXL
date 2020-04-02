// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Rush.ProjectGraph
{
    /// <summary>
    /// Useful for deserializing a project graph where dependencies are specified as strings, 
    /// </summary>
    /// <remarks>
    /// There is a 1:1 mapping between a DeserializedRushProject and and a package.json. All specified
    /// script commands are available since the project-to-project graph is agnostic to the requested script commands.
    /// </remarks>
    [DebuggerDisplay("{Name}")]
    public sealed class DeserializedRushProject : GenericRushProject<string>
    {
        /// <nodoc/>
        public DeserializedRushProject(
            string name,
            AbsolutePath projectFolder,
            IReadOnlyCollection<string> dependencies,
            [CanBeNull] IReadOnlyDictionary<string, string> availableScriptCommands,
            AbsolutePath tempFolder,
            IReadOnlyCollection<AbsolutePath> additionalOutputDirectories) : base(name, projectFolder, dependencies, tempFolder, additionalOutputDirectories)
        {
            Contract.RequiresNotNull(dependencies);

            AvailableScriptCommands = availableScriptCommands ?? CollectionUtilities.EmptyDictionary<string, string>();
        }

        /// <summary>
        /// The script commands that are available for the project
        /// </summary>
        public IReadOnlyDictionary<string, string> AvailableScriptCommands { get; }
    }
}
