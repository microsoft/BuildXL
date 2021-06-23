// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.JavaScript.ProjectGraph
{
    /// <summary>
    /// There is a 1:1 relationship between a JavaScript project and a tuple (package.json, requested script command name)
    /// </summary>
    [DebuggerDisplay("{Name}-{ScriptCommandName}")]
    public sealed class JavaScriptProject : GenericJavaScriptProject<JavaScriptProject>, IProjectWithDependencies<JavaScriptProject>
    {
        private readonly HashSet<FileArtifact> m_inputFiles = new HashSet<FileArtifact>();
        private readonly HashSet<DirectoryArtifact> m_inputDirectories = new HashSet<DirectoryArtifact>();

        /// <nodoc/>
        public JavaScriptProject(
            string name,
            AbsolutePath projectFolder,
            string scriptCommandName,
            [CanBeNull] string scriptCommand,
            AbsolutePath tempFolder,
            IReadOnlyCollection<AbsolutePath> outputDirectories,
            IEnumerable<FileArtifact> inputFiles,
            IEnumerable<DirectoryArtifact> inputDirectories) : base(name, projectFolder, null, tempFolder)
        {
            Contract.RequiresNotNullOrEmpty(scriptCommandName);
            Contract.RequiresNotNull(outputDirectories);
            Contract.RequiresNotNull(inputFiles);
            Contract.RequiresNotNull(inputDirectories);

            ScriptCommand = scriptCommand;
            ScriptCommandName = scriptCommandName;
            OutputDirectories = outputDirectories;
            m_inputFiles.AddRange(inputFiles);
            m_inputDirectories.AddRange(inputDirectories);
        }

        /// <nodoc/>
        public static JavaScriptProject FromDeserializedProject(string scriptCommandName, string scriptCommand, DeserializedJavaScriptProject deserializedJavaScriptProject)
        {
            // Filter the output directories and source files that apply to this particular script command name
            var outputDirectories = ExtractRelevantPaths(scriptCommandName, deserializedJavaScriptProject.OutputDirectories);
            var inputFiles = ExtractRelevantPaths(scriptCommandName, deserializedJavaScriptProject.SourceFiles).Select(path => FileArtifact.CreateSourceFile(path)).ToList();

            return new JavaScriptProject(
                deserializedJavaScriptProject.Name,
                deserializedJavaScriptProject.ProjectFolder,
                scriptCommandName,
                scriptCommand,
                deserializedJavaScriptProject.TempFolder,
                outputDirectories,
                inputFiles,
                inputDirectories: CollectionUtilities.EmptyArray<DirectoryArtifact>());
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

        /// <nodoc/>
        public void AddInputFile(FileArtifact file)
        {
            m_inputFiles.Add(file);
        }

        /// <nodoc/>
        public void AddInputDirectory(DirectoryArtifact directory)
        {
            m_inputDirectories.Add(directory);
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
        public IReadOnlyCollection<FileArtifact> InputFiles => m_inputFiles;

        /// <nodoc/>
        public IReadOnlyCollection<DirectoryArtifact> InputDirectories => m_inputDirectories;
    }
}
