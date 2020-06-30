// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.JavaScript.ProjectGraph
{
    /// <summary>
    /// There is a 1:1 relationship between a JavaScript project and a tuple (package.json, requested script command name)
    /// </summary>
    [DebuggerDisplay("{Name}")]
    public sealed class JavaScriptProject : GenericJavaScriptProject<JavaScriptProject>, IProjectWithDependencies<JavaScriptProject>
    {
        /// <nodoc/>
        public JavaScriptProject(
            string name,
            AbsolutePath projectFolder,
            string scriptCommandName,
            [CanBeNull] string scriptCommand,
            AbsolutePath tempFolder,
            IReadOnlyCollection<AbsolutePath> outputDirectories,
            IReadOnlyCollection<AbsolutePath> sourceFiles) : base(name, projectFolder, null, tempFolder)
        {
            Contract.RequiresNotNullOrEmpty(scriptCommandName);
            Contract.RequiresNotNull(outputDirectories);
            Contract.RequiresNotNull(sourceFiles);

            ScriptCommand = scriptCommand;
            ScriptCommandName = scriptCommandName;
            OutputDirectories = outputDirectories;
            SourceFiles = sourceFiles;
        }

        /// <nodoc/>
        public static JavaScriptProject FromDeserializedProject(string scriptCommandName, string scriptCommand, DeserializedJavaScriptProject deserializedJavaScriptProject, PathTable pathTable)
        {
            // Filter the output directories and source files that apply to this particular script command name
            var outputDirectories = ExtractRelevantPaths(scriptCommandName, deserializedJavaScriptProject.OutputDirectories);
            var sourceFiles = ExtractRelevantPaths(scriptCommandName, deserializedJavaScriptProject.SourceFiles);

            return new JavaScriptProject(
                deserializedJavaScriptProject.Name,
                deserializedJavaScriptProject.ProjectFolder,
                scriptCommandName,
                scriptCommand,
                deserializedJavaScriptProject.TempFolder,
                outputDirectories,
                sourceFiles);
        }

        private static List<AbsolutePath> ExtractRelevantPaths(
            string scriptCommandName, 
            IReadOnlyCollection<PathWithTargets> paths)
        {
            return paths
                .Where(pathWithTargets => pathWithTargets.AppliesToScript(scriptCommandName))
                .Select(pathWithTargets => pathWithTargets.Path)
                .ToList();
        }

        /// <nodoc/>
        public void SetDependencies(IReadOnlyCollection<JavaScriptProject> dependencies)
        {
            Dependencies = dependencies;
        }

        /// <summary>
        /// A JavaScript project can be scheduled is the script command is not empty
        /// </summary>
        public bool CanBeScheduled() => !string.IsNullOrEmpty(ScriptCommand);

        /// <summary>
        /// The script command to execute for this particular project (e.g. 'node ./main.js')
        /// </summary>
        public string ScriptCommand { get; }

        /// <summary>
        /// The script command name to execute for this particular project (e.g. 'build' or 'test')
        /// </summary>
        public string ScriptCommandName { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{Name}[{ScriptCommandName}]";
        }

        /// <nodoc/>
        public IReadOnlyCollection<AbsolutePath> OutputDirectories { get; }

        /// <nodoc/>
        public IReadOnlyCollection<AbsolutePath> SourceFiles { get; }
    }
}
