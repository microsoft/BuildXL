// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using System.Diagnostics.CodeAnalysis;

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
        private readonly string m_projectNameDisplayString;

        /// <nodoc/>
        public JavaScriptProject(
            string name,
            AbsolutePath projectFolder,
            string scriptCommandName,
            [AllowNull] string scriptCommand,
            AbsolutePath tempFolder,
            IReadOnlyCollection<AbsolutePath> outputDirectories,
            IEnumerable<FileArtifact> inputFiles,
            IEnumerable<DirectoryArtifact> inputDirectories,
            IEnumerable<AbsolutePath> sourceDirectories,
            bool cacheable,
            [AllowNull] string projectNameDisplayString = null,
            int timeoutInMilliseconds = 0,
            int warningTimeoutInMilliseconds = 0) : base(name, projectFolder, null, tempFolder, cacheable, timeoutInMilliseconds, warningTimeoutInMilliseconds)
        {
            Contract.RequiresNotNullOrEmpty(scriptCommandName);
            Contract.RequiresNotNull(outputDirectories);
            Contract.RequiresNotNull(inputFiles);
            Contract.RequiresNotNull(inputDirectories);
            Contract.RequiresNotNull(sourceDirectories);

            ScriptCommand = scriptCommand;
            ScriptCommandName = scriptCommandName;
            OutputDirectories = outputDirectories;
            m_inputFiles.AddRange(inputFiles);
            m_inputDirectories.AddRange(inputDirectories);
            m_projectNameDisplayString = projectNameDisplayString;
            SourceDirectories = sourceDirectories.ToList();
        }

        /// <nodoc/>
        public static JavaScriptProject FromDeserializedProject(string scriptCommandName, string scriptCommand, DeserializedJavaScriptProject deserializedJavaScriptProject, string projectNameDisplayString = null)
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
                inputDirectories: CollectionUtilities.EmptyArray<DirectoryArtifact>(),
                sourceDirectories: deserializedJavaScriptProject.SourceDirectories,
                deserializedJavaScriptProject.Cacheable,
                projectNameDisplayString,
                deserializedJavaScriptProject.TimeoutInMilliseconds,
                deserializedJavaScriptProject.WarningTimeoutInMilliseconds);
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

        /// <summary>
        /// Human readable project name used for UI rendering
        /// </summary>
        /// <remarks>
        /// If not specified on construction, the project name is used instead
        /// </remarks>
        public string ProjectNameDisplayString => m_projectNameDisplayString ?? Name;

        /// <nodoc/>
        public IReadOnlyCollection<AbsolutePath> OutputDirectories { get; }

        /// <nodoc/>
        public IReadOnlyCollection<FileArtifact> InputFiles => m_inputFiles;

        /// <nodoc/>
        public IReadOnlyCollection<DirectoryArtifact> InputDirectories => m_inputDirectories;

        /// <summary>
        /// Directory scopes the project is allowed to read from.
        /// </summary>
        /// <remarks>
        /// Only honored when EnforceSourceReadsUnderPackageRoots knob in the resolver settings is enabled.
        /// </remarks>
        public IReadOnlyCollection<AbsolutePath> SourceDirectories { get; }
    }
}
