// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <summary>
    /// Creates a pip based on a <see cref="JavaScriptProject"/>
    /// </summary>
    /// <remarks>
    /// Allows to extend its behavior for other JavaScript-based coordinators by 
    /// * extending how inputs and outputs: <see cref="ProcessInputs(JavaScriptProject, ProcessBuilder)"/>, <see cref="ProcessOutputs(JavaScriptProject, ProcessBuilder)"/>
    /// * extending how the process builder is configured and arguments are built: <see cref="ConfigureProcessBuilder(ProcessBuilder, JavaScriptProject)"/>
    /// * and by extending how the environment for each pip is created: <see cref="DoCreateEnvironment(JavaScriptProject)"/>
    /// </remarks>
    public class JavaScriptPipConstructor : IProjectToPipConstructor<JavaScriptProject> 
    {
        private readonly FrontEndContext m_context;

        private readonly FrontEndHost m_frontEndHost;
        private readonly ModuleDefinition m_moduleDefinition;
        private readonly IJavaScriptResolverSettings m_resolverSettings;

        private AbsolutePath Root => m_resolverSettings.Root;

        private readonly IEnumerable<KeyValuePair<string, string>> m_userDefinedEnvironment;
        private readonly IEnumerable<string> m_userDefinedPassthroughVariables;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<JavaScriptArgument>> m_customCommands;
        private readonly IEnumerable<AbsolutePath> m_allProjectRoots;

        /// <nodoc/>
        protected PathTable PathTable => m_context.PathTable;

        private readonly ConcurrentBigMap<JavaScriptProject, ProcessOutputs> m_processOutputsPerProject = new ConcurrentBigMap<JavaScriptProject, ProcessOutputs>();

        private readonly ConcurrentBigMap<JavaScriptProject, IReadOnlySet<JavaScriptProject>> m_transitiveDependenciesPerProject = new ConcurrentBigMap<JavaScriptProject, IReadOnlySet<JavaScriptProject>>();

        private readonly ConcurrentBigMap<(JavaScriptProject, QualifierId), PipConstructionHelper> m_pipConstructionHelperPerProject = new ConcurrentBigMap<(JavaScriptProject, QualifierId), PipConstructionHelper>();

        private readonly ConcurrentBigSet<AbsolutePath> m_specFilePips = new ConcurrentBigSet<AbsolutePath>();

        /// <summary>
        /// Base directory where all logs are located
        /// </summary>
        internal static AbsolutePath LogDirectoryBase(IConfiguration configuration, PathTable pathTable, string resolverName) => configuration.Logging.RedirectedLogsDirectory
                .Combine(pathTable, resolverName);

        /// <nodoc/>
        public JavaScriptPipConstructor(
            FrontEndContext context,
            FrontEndHost frontEndHost,
            ModuleDefinition moduleDefinition,
            IJavaScriptResolverSettings resolverSettings,
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment,
            IEnumerable<string> userDefinedPassthroughVariables,
            IReadOnlyDictionary<string, IReadOnlyList<JavaScriptArgument>> customCommands,
            IEnumerable<JavaScriptProject> allProjectsToBuild)
        {
            Contract.RequiresNotNull(context);
            Contract.RequiresNotNull(frontEndHost);
            Contract.RequiresNotNull(moduleDefinition);
            Contract.RequiresNotNull(resolverSettings);
            Contract.RequiresNotNull(userDefinedEnvironment);
            Contract.RequiresNotNull(userDefinedPassthroughVariables);
            Contract.RequiresNotNull(customCommands);
            Contract.RequiresNotNull(allProjectsToBuild);

            m_context = context;
            m_frontEndHost = frontEndHost;
            m_moduleDefinition = moduleDefinition;
            m_resolverSettings = resolverSettings;
            m_userDefinedEnvironment = userDefinedEnvironment;
            m_userDefinedPassthroughVariables = userDefinedPassthroughVariables;
            m_customCommands = customCommands;
            m_allProjectRoots = allProjectsToBuild.Select(project => project.ProjectFolder);
        }

        /// <summary>
        /// Creates a pip corresponding to the provided project and qualifier
        /// </summary>
        public Possible<ProjectCreationResult<JavaScriptProject>> TryCreatePipForProject(JavaScriptProject project, QualifierId qualifierId)
        {
            // We create a pip construction helper for each project
            var pipConstructionHelper = GetPipConstructionHelperForProject(project, qualifierId);

            try
            {
                if (!TryCreateProcess(pipConstructionHelper, project, out string failureDetail, out Process process, out ProcessOutputs processOutputs))
                {
                    Tracing.Logger.Log.SchedulingPipFailure(
                            m_context.LoggingContext,
                            Location.FromFile(project.ProjectFolder.ToString(PathTable)),
                            failureDetail);

                    return new JavaScriptProjectSchedulingFailure(project, failureDetail);
                }

                return new ProjectCreationResult<JavaScriptProject>(project, process, processOutputs);
            }
            catch (Exception ex)
            {
                Tracing.Logger.Log.UnexpectedPipBuilderException(
                    m_context.LoggingContext,
                    Location.FromFile(project.ProjectFolder.ToString(PathTable)),
                    ex.GetLogEventMessage(),
                    ex.StackTrace);

                return new JavaScriptProjectSchedulingFailure(project, ex.ToString());
            }
        }

        /// <summary>
        /// Adds the created project to the graph
        /// </summary>
        /// <remarks>
        /// The project is assumed to be scheduled in the right order, where all dependencies are scheduled first.
        /// See topographical sort performed in <see cref="ProjectGraphToPipGraphConstructor{JavaScriptProject}"/>.
        /// </remarks>
        public Possible<ProcessOutputs> TrySchedulePipForProject(
            ProjectCreationResult<JavaScriptProject> creationResult,
            QualifierId qualifierId)
        {
            // We create a pip construction helper for each project
            var project = creationResult.Project;
            var pipConstructionHelper = GetPipConstructionHelperForProject(project, qualifierId);

            try
            {
                // Try to add the process pip to the graph
                if (!pipConstructionHelper.TryAddFinishedProcessToGraph(creationResult.Process, creationResult.Outputs))
                {
                    string failureDetail = "Failed to add the pip";
                    Tracing.Logger.Log.SchedulingPipFailure(
                                m_context.LoggingContext,
                                Location.FromFile(project.ProjectFolder.ToString(PathTable)),
                                failureDetail);

                    return new JavaScriptProjectSchedulingFailure(project, failureDetail);
                }

                m_processOutputsPerProject[creationResult.Project] = creationResult.Outputs;

                return creationResult.Outputs;
            }
            catch (Exception ex)
            {
                Tracing.Logger.Log.UnexpectedPipBuilderException(
                    m_context.LoggingContext,
                    Location.FromFile(project.ProjectFolder.ToString(PathTable)),
                    ex.GetLogEventMessage(),
                    ex.StackTrace);

                return new JavaScriptProjectSchedulingFailure(project, ex.ToString());
            }
            finally 
            {
                // Once the pip is added to the graph it should be safe to remove the pip construction helper from the cache
                m_pipConstructionHelperPerProject.TryRemove((project, qualifierId), out _);
            }
        }

        /// <inheritdoc/>
        public void NotifyCustomProjectScheduled(JavaScriptProject project, ProcessOutputs outputs, QualifierId qualifierId)
        {
            m_processOutputsPerProject[project] = outputs;
        }

        private IReadOnlyDictionary<string, string> CreateEnvironment(JavaScriptProject project)
        {
            return DoCreateEnvironment(project);
        }

        /// <summary>
        /// Creates the environment each pip is exposed to during execution
        /// </summary>
        protected virtual Dictionary<string, string> DoCreateEnvironment(JavaScriptProject project)
        {
            var env = new Dictionary<string, string>(OperatingSystemHelper.EnvVarComparer);

            //
            // Initial environment variables that may be overwritten by the outer environment.
            //

            // Observe there is no need to inform the engine this environment is being used since
            // the same environment was used during graph construction, and the engine is already tracking them
            foreach (var input in m_userDefinedEnvironment)
            {
                string envVarName = input.Key;

                // Temp directory entries are added at pip creation time.
                if (BuildParameters.DisallowedTempVariables.Contains(envVarName.ToUpper()))
                {
                    continue;
                }

                env[envVarName] = input.Value;
            }

            // node_modules/.bin is expected to be part of the project path. This is standard for JavaScript projects.
            string nodeModulesBin = project.ProjectFolder.Combine(PathTable, RelativePath.Create(PathTable.StringTable, "node_modules/.bin")).ToString(PathTable);
            env["PATH"] = nodeModulesBin + (env.ContainsKey("PATH")? $";{env["PATH"]}" : string.Empty);
            
            return env;
        }

        private bool TryCreateProcess(
            PipConstructionHelper pipConstructionHelper,
            JavaScriptProject project,
            out string failureDetail,
            out Process process,
            out ProcessOutputs processOutputs)
        {
            using (var processBuilder = ProcessBuilder.Create(PathTable, m_context.GetPipDataBuilder(), m_frontEndHost.Configuration))
            {
                // Configure the process to add an assortment of settings: arguments, response file, etc.
                ConfigureProcessBuilder(processBuilder, project);

                // Process all predicted outputs and inputs, including the predicted project dependencies
                ProcessInputs(project, processBuilder);
                ProcessOutputs(project, processBuilder);

                // Try to create the process pip
                if (!pipConstructionHelper.TryFinishProcessApplyingOSDefaults(processBuilder, out processOutputs, out process))
                {
                    failureDetail = "Failed to create the pip";
                    return false;
                }

                failureDetail = string.Empty;
                return true;
            }
        }

        /// <summary>
        /// Adds all predicted dependencies as inputs, plus all individual inputs predicted for the project
        /// </summary>
        /// <remarks>
        /// Adding all predicted dependencies is key to get the right scheduling. On the other hand, all predicted inputs
        /// are not really needed since we are running in undeclared read mode. However, they contribute to make the weak fingerprint stronger (that
        /// otherwise will likely be just a shared opaque output at the root).
        /// </remarks>
        protected virtual void ProcessInputs(
            JavaScriptProject project,
            ProcessBuilder processBuilder)
        {
            // Add all explicitly declared source files
            foreach (AbsolutePath sourceFile in project.SourceFiles)
            {
                processBuilder.AddInputFile(FileArtifact.CreateSourceFile(sourceFile));
            }

            // Add package.json, which should always be present at the root of the project
            processBuilder.AddInputFile(FileArtifact.CreateSourceFile(project.PackageJsonFile(PathTable)));

            // In this case all the transitive closure is automatically exposed to the project as direct references. This is standard for
            // JavaScript projects.
            var transitiveReferences = new HashSet<JavaScriptProject>();
            ComputeTransitiveDependenciesFor(project, transitiveReferences);
            IEnumerable<JavaScriptProject> references = transitiveReferences;

            foreach (JavaScriptProject projectReference in references)
            {
                // If the project is referencing something that was not scheduled, just skip it
                if (!projectReference.CanBeScheduled())
                {
                    // We have already logged this case as an informational when building the project graph
                    continue;
                }

                bool outputsPresent = m_processOutputsPerProject.TryGetValue(projectReference, out var processOutputs);
                if (!outputsPresent)
                {
                    Contract.Assert(false, $"Pips must have been presented in dependency order: {projectReference.ProjectFolder.ToString(PathTable)} missing, dependency of {project.ProjectFolder.ToString(PathTable)}");
                }

                // Add all known output directories
                foreach (StaticDirectory output in processOutputs.GetOutputDirectories())
                {
                    processBuilder.AddInputDirectory(output.Root);
                }
                
                // Add all known output files, but exclude logs
                foreach (FileArtifact output in processOutputs.GetOutputFiles()
                    .Where(fa => !fa.Path.IsWithin(m_context.PathTable, LogDirectoryBase(m_frontEndHost.Configuration, m_context.PathTable, m_resolverSettings.Name))))
                {
                    processBuilder.AddInputFile(output);
                }
            }
        }

        /// <summary>
        /// Adds all known output directories
        /// </summary>
        /// <remarks>
        /// The project root (excluding node_modules) is always considered an output directory
        /// </remarks>
        protected virtual void ProcessOutputs(JavaScriptProject project, ProcessBuilder processBuilder)
        {
            // Each project is automatically allowed to write anything under its project root
            processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(project.ProjectFolder), SealDirectoryKind.SharedOpaque);

            if (m_resolverSettings.BlockWritesUnderNodeModules == true)
            {
                // There shouldn't be any writes under node_modules. So exclude it explicitly, since that also avoids a usually expensive enumeration
                // under node_modules when scrubbing. 
                processBuilder.AddOutputDirectoryExclusion(project.NodeModulesFolder(m_context.PathTable));
            }

            // Some projects share their temp folder across their build scripts (e.g. build and test)
            // So we cannot make them share the temp folder with the infrastructure we have today
            // (even though not impossible to fix, we could allow temp directories among pips that are part
            // of the same depedency chain and eagerly delete the folder every time a pip finishes)
            processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(project.TempFolder), SealDirectoryKind.SharedOpaque);

            // Add all the additional output directories that the graph knows about
            foreach (var outputDirectory in project.OutputDirectories)
            {
                processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(outputDirectory), SealDirectoryKind.SharedOpaque);
            }

            // Add additional output directories configured in the main config file
            AddAdditionalOutputDirectories(processBuilder, project.ProjectFolder);
        }

        private void ComputeTransitiveDependenciesFor(JavaScriptProject project, HashSet<JavaScriptProject> accumulatedDependencies)
        {
            // We already computed the transitive dependencies for the required project
            if (m_transitiveDependenciesPerProject.TryGetValue(project, out var transitiveDependencies))
            {
                accumulatedDependencies.AddRange(transitiveDependencies);
                return;
            }

            foreach (JavaScriptProject dependency in project.Dependencies)
            {
                accumulatedDependencies.Add(dependency);
                ComputeTransitiveDependenciesFor(dependency, accumulatedDependencies);
            }

            m_transitiveDependenciesPerProject.TryAdd(project, accumulatedDependencies.ToReadOnlySet());
        }

        /// <summary>
        /// Configures the process builder to execute the specified commands
        /// </summary>
        protected virtual void ConfigureProcessBuilder(
            ProcessBuilder processBuilder,
            JavaScriptProject project)
        {
            SetCmdTool(processBuilder, project);

            // Working directory - the directory where the project file lives.
            processBuilder.WorkingDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(project.ProjectFolder);

            // We allow undeclared inputs to be read
            processBuilder.Options |= Process.Options.AllowUndeclaredSourceReads;

            // We want to enforce the use of weak fingerprint augmentation since input predictions could be not complete/sufficient
            // to avoid a large number of path sets
            processBuilder.Options |= Process.Options.EnforceWeakFingerprintAugmentation;

            // Try to preserve path set casing since many JavaScript projects deal with paths in a case-sensitive way
            // Otherwise in Windows we force path sets to be all uppercase
            processBuilder.Options |= Process.Options.PreservePathSetCasing;

            // By default the double write policy is to allow same content double writes and safe rewrites.
            // Otherwise we honor the double write policy specified in the resolver configuration
            processBuilder.RewritePolicy |= m_resolverSettings.DoubleWritePolicy.HasValue ? 
                (m_resolverSettings.DoubleWritePolicy.Value | RewritePolicy.SafeSourceRewritesAreAllowed) : 
                RewritePolicy.DefaultSafe;

            // Untrack the user profile. The corresponding mount is already configured for not tracking source files, and with allowed undeclared source reads,
            // any attempt to read into the user profile will fail to compute its corresponding hash
            processBuilder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.UserProfile)));

            // Add the associated build script name as a tag, so filtering on 'build' or 'test' can happen
            processBuilder.Tags = ReadOnlyArray<StringId>.FromWithoutCopy(new[] { StringId.Create(m_context.StringTable, project.ScriptCommandName) });

            // Configure the pip to fail if stderr is written. Defaults to false if not explicitly configured.
            if (m_resolverSettings.WritingToStandardErrorFailsExecution == true)
            {
                processBuilder.Options |= Process.Options.WritingToStandardErrorFailsExecution;
            }

            PipConstructionUtilities.UntrackUserConfigurableArtifacts(m_context.PathTable, project.ProjectFolder, m_allProjectRoots, processBuilder, m_resolverSettings);

            var logDirectory = GetLogDirectory(project);
            processBuilder.SetStandardOutputFile(logDirectory.Combine(m_context.PathTable, "build.log"));
            processBuilder.SetStandardErrorFile(logDirectory.Combine(m_context.PathTable, "error.log"));

            using (processBuilder.ArgumentsBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
            {
                processBuilder.ArgumentsBuilder.Add(PipDataAtom.FromString("/C"));

                using (processBuilder.ArgumentsBuilder.StartFragment(PipDataFragmentEscaping.NoEscaping, " "))
                {
                    // Execute the command and redirect the output to a designated log file
                    processBuilder.ArgumentsBuilder.Add(PipDataAtom.FromString(project.ScriptCommand));

                    // If we need to append arguments to the script command, do it here
                    if (m_customCommands.TryGetValue(project.ScriptCommandName, out IReadOnlyList<JavaScriptArgument> extraArguments))
                    {
                        foreach (JavaScriptArgument value in extraArguments)
                        {
                            AddJavaScriptArgumentToBuilder(processBuilder.ArgumentsBuilder, value);
                        }
                    }
                }
            }

            FrontEndUtilities.SetProcessEnvironmentVariables(CreateEnvironment(project), m_userDefinedPassthroughVariables, processBuilder, m_context.PathTable);
        }

        private void AddJavaScriptArgumentToBuilder(PipDataBuilder argumentsBuilder, JavaScriptArgument value)
        {
            switch (value.GetValue())
            {
                case string s:
                    using (argumentsBuilder.StartFragment(PipDataFragmentEscaping.NoEscaping, m_context.StringTable.Empty))
                    {
                        argumentsBuilder.Add(s);
                    }
                    break;
                case AbsolutePath absolutePath:
                    using (argumentsBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, m_context.StringTable.Empty))
                    {
                        argumentsBuilder.Add(absolutePath);
                    }
                    break;
                case RelativePath relativePath:
                    using (argumentsBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, m_context.StringTable.Empty))
                    {
                        argumentsBuilder.Add(relativePath);
                    }
                    break;
                case PathAtom pathAtom:
                    using (argumentsBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, m_context.StringTable.Empty))
                    {
                        argumentsBuilder.Add(pathAtom);
                    }
                    break;
                default:
                    Contract.Assert(false, $"Unexpected argument '{value.GetType()}'");
                    break;
            }
        }

        private void AddAdditionalOutputDirectories(ProcessBuilder processBuilder, AbsolutePath projectFolder)
        {
            if (m_resolverSettings.AdditionalOutputDirectories == null)
            {
                return;
            }

            foreach (DiscriminatingUnion<AbsolutePath, RelativePath> directoryUnion in m_resolverSettings.AdditionalOutputDirectories)
            {
                object directory = directoryUnion.GetValue();
                if (directory is AbsolutePath absolutePath)
                {
                    processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(absolutePath), SealDirectoryKind.SharedOpaque);
                }
                else
                {
                    // The specified relative path is interpreted relative to the project directory folder
                    AbsolutePath absoluteDirectory = projectFolder.Combine(PathTable, (RelativePath)directory);
                    processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(absoluteDirectory), SealDirectoryKind.SharedOpaque);
                }
            }
        }

        private AbsolutePath GetLogDirectory(JavaScriptProject projectFile)
        {
            var success = Root.TryGetRelative(PathTable, projectFile.ProjectFolder, out var inFolderPathFromEnlistmentRoot);
            Contract.Assert(success, $"Configuration root '{Root.ToString(PathTable)}' should be a parent of '{projectFile.ProjectFolder.ToString(PathTable)}'");

            // We hardcode the log to go under the output directory Logs/<frontend-name> (and follow the project structure underneath)
            var result = LogDirectoryBase(m_frontEndHost.Configuration, m_context.PathTable, m_resolverSettings.Name)
                .Combine(PathTable, inFolderPathFromEnlistmentRoot)
                .Combine(PathTable, PipConstructionUtilities.SanitizeStringForSymbol(projectFile.Name))
                .Combine(PathTable, PipConstructionUtilities.SanitizeStringForSymbol(projectFile.ScriptCommandName));

            return result;
        }

        private void SetCmdTool(
            ProcessBuilder processBuilder,
            JavaScriptProject project)
        {
            var cmdExeArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(PathTable, Environment.GetEnvironmentVariable("COMSPEC")));

            processBuilder.Executable = cmdExeArtifact;
            processBuilder.AddInputFile(cmdExeArtifact);
            processBuilder.AddCurrentHostOSDirectories();
            processBuilder.AddUntrackedAppDataDirectories();
            processBuilder.AddUntrackedProgramDataDirectories();

            // Temp directory setup including setting TMP and TEMP env vars. The path to
            // the temp dir is generated in a consistent fashion between BuildXL runs to
            // ensure environment value (and hence pip hash) consistency.
            processBuilder.EnableTempDirectory();

            processBuilder.ToolDescription = StringId.Create(m_context.StringTable, I($"{m_moduleDefinition.Descriptor.Name} - {project.Name}"));
        }

        private PipConstructionHelper GetPipConstructionHelperForProject(JavaScriptProject project, QualifierId qualifierId)
        {
            // Check the cache first
            var cachedResult = m_pipConstructionHelperPerProject.TryGet((project, qualifierId));
            if (cachedResult.IsFound)
            {
                return cachedResult.Item.Value;
            }

            var pathToProject = project.ProjectFolder;

            // We shouldn't be adding the same spec file pip to the pip graph.
            // This can happen if the same package root is defined for multiple packages, or if different qualifiers apply to the same package.
            var result = m_specFilePips.GetOrAdd(pathToProject);
            if (!result.IsFound)
            {
                m_frontEndHost.PipGraph?.AddSpecFile(
                    new SpecFilePip(
                        FileArtifact.CreateSourceFile(pathToProject),
                        new LocationData(pathToProject, 0, 0),
                        m_moduleDefinition.Descriptor.Id));
            }

            Root.TryGetRelative(PathTable, pathToProject, out var specRelativePath);
            if (!PathAtom.TryCreate(m_context.StringTable, m_moduleDefinition.Descriptor.Name, out _))
            {
                throw new ArgumentException($"Failed to create PathAtom from {m_moduleDefinition.Descriptor.Name}");
            }

            // Get a symbol that is unique for this particular project instance
            var fullSymbol = GetFullSymbolFromProject(project.Name, project.ScriptCommandName, m_context.SymbolTable);

            var pipConstructionHelper = PipConstructionHelper.Create(
                m_context,
                m_frontEndHost.Engine.Layout.ObjectDirectory,
                m_frontEndHost.Engine.Layout.RedirectedDirectory,
                m_frontEndHost.Engine.Layout.TempDirectory,
                m_frontEndHost.PipGraph,
                m_moduleDefinition.Descriptor.Id,
                m_moduleDefinition.Descriptor.Name,
                specRelativePath,
                fullSymbol,
                new LocationData(pathToProject, 0, 0),
                qualifierId);

            // Update the pip construction helper cache
            m_pipConstructionHelperPerProject.TryAdd((project, qualifierId), pipConstructionHelper);

            return pipConstructionHelper;
        }

        /// <summary>
        /// Used also for tests to discover processes based on project names
        /// </summary>
        internal static FullSymbol GetFullSymbolFromProject(string projectName, string scriptCommandName, SymbolTable symbolTable)
        {
            // We construct the name of the value using the project name
            var valueName = PipConstructionUtilities.SanitizeStringForSymbol($"{projectName}_{scriptCommandName}");

            var fullSymbol = FullSymbol.Create(symbolTable, valueName);
            return fullSymbol;
        }

        /// <inheritdoc/>
        public void NotifyProjectNotScheduled(JavaScriptProject project)
        {
            // TODO: add logging
        }
    }
}
