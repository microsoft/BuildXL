// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Rush.ProjectGraph
{
    /// <summary>
    /// A Rush project is a projection of a RushConfigurationProject (defined in rush-core-lib) with enough 
    /// information to schedule a pip from it
    /// </summary>
    /// <remarks>
    /// There is a 1:1 relationship between a Rush project and a tuple (package.json, requested script command name)
    /// </remarks>
    [DebuggerDisplay("{Name}")]
    public sealed class RushProject : GenericRushProject<RushProject>, IProjectWithDependencies<RushProject>
    {
        /// <nodoc/>
        public RushProject(
            string name,
            AbsolutePath projectFolder,
            string scriptCommandName,
            [CanBeNull] string scriptCommand,
            AbsolutePath tempFolder,
            IReadOnlyCollection<AbsolutePath> additionalOutputDirectories) : base(name, projectFolder, null, tempFolder, additionalOutputDirectories)
        {
            Contract.RequiresNotNullOrEmpty(scriptCommandName);

            ScriptCommand = scriptCommand;
            ScriptCommandName = scriptCommandName;
        }

        /// <nodoc/>
        public static RushProject FromDeserializedProject(string scriptCommandName, string scriptCommand, DeserializedRushProject deserializedRushProject)
        {
            return new RushProject(
                deserializedRushProject.Name,
                deserializedRushProject.ProjectFolder,
                scriptCommandName,
                scriptCommand,
                deserializedRushProject.TempFolder,
                deserializedRushProject.AdditionalOutputDirectories);
        }

        /// <nodoc/>
        public void SetDependencies(IReadOnlyCollection<RushProject> dependencies)
        {
            Dependencies = dependencies;
        }

        /// <summary>
        /// A rush project can be scheduled is the script command is not empty
        /// </summary>
        public bool CanBeScheduled() => !string.IsNullOrEmpty(ScriptCommand);

        /// <summary>
        /// The script command to execute for this particular rush project (e.g. 'node ./main.js')
        /// </summary>
        public string ScriptCommand { get; }

        /// <summary>
        /// The script command name to execute for this particular rush project (e.g. 'build' or 'test')
        /// </summary>
        public string ScriptCommandName { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{Name}[{ScriptCommandName}]";
        }
    }
}
