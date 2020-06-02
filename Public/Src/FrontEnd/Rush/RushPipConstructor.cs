// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.MsBuild;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
using BuildXL.FrontEnd.Sdk;
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

namespace BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Creates a pip based on a <see cref="RushProject"/>
    /// </summary>
    internal sealed class RushPipConstructor : IProjectToPipConstructor<RushProject>
    {
        private readonly FrontEndContext m_context;

        private readonly FrontEndHost m_frontEndHost;
        private readonly ModuleDefinition m_moduleDefinition;
        private readonly RushConfiguration m_rushConfiguration;
        private readonly IRushResolverSettings m_resolverSettings;

        private AbsolutePath Root => m_resolverSettings.Root;

        private readonly IEnumerable<KeyValuePair<string, string>> m_userDefinedEnvironment;
        private readonly IEnumerable<string> m_userDefinedPassthroughVariables;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<RushArgument>> m_customCommands;

        private PathTable PathTable => m_context.PathTable;

        private readonly ConcurrentDictionary<RushProject, ProcessOutputs> m_processOutputsPerProject = new ConcurrentDictionary<RushProject, ProcessOutputs>();

        private readonly ConcurrentBigMap<RushProject, IReadOnlySet<RushProject>> m_transitiveDependenciesPerProject = new ConcurrentBigMap<RushProject, IReadOnlySet<RushProject>>();

        /// <summary>
        /// Base directory where all rush logs are located
        /// </summary>
        internal static AbsolutePath LogDirectoryBase(IConfiguration configuration, PathTable pathTable) => configuration.Layout.OutputDirectory
                .Combine(pathTable, "Logs")
                .Combine(pathTable, "Rush");

        /// <summary>
        /// Project-specific user profile folder
        /// </summary>
        internal static AbsolutePath UserProfile(RushProject project, PathTable pathTable) => project.TempFolder
            .Combine(pathTable, "USERPROFILE")
            .Combine(pathTable, project.ScriptCommandName);

        /// <nodoc/>
        public RushPipConstructor(
            FrontEndContext context,
            FrontEndHost frontEndHost,
            ModuleDefinition moduleDefinition,
            RushConfiguration rushConfiguration,
            IRushResolverSettings resolverSettings,
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment,
            IEnumerable<string> userDefinedPassthroughVariables,
            IReadOnlyDictionary<string, IReadOnlyList<RushArgument>> customCommands)
        {
            Contract.RequiresNotNull(context);
            Contract.RequiresNotNull(frontEndHost);
            Contract.RequiresNotNull(moduleDefinition);
            Contract.RequiresNotNull(resolverSettings);
            Contract.RequiresNotNull(userDefinedEnvironment);
            Contract.RequiresNotNull(userDefinedPassthroughVariables);
            Contract.RequiresNotNull(customCommands);

            m_context = context;
            m_frontEndHost = frontEndHost;
            m_moduleDefinition = moduleDefinition;
            m_rushConfiguration = rushConfiguration;
            m_resolverSettings = resolverSettings;
            m_userDefinedEnvironment = userDefinedEnvironment;
            m_userDefinedPassthroughVariables = userDefinedPassthroughVariables;
            m_customCommands = customCommands;
        }

        /// <summary>
        /// Schedules a pip corresponding to the provided project and qualifier
        /// </summary>
        /// <remarks>
        /// The project is assumed to be scheduled in the right order, where all dependencies are scheduled first.
        /// See topographical sort performed in <see cref="ProjectGraphToPipGraphConstructor{TProject}"/>.
        /// </remarks>
        public Possible<Process> TrySchedulePipForProject(RushProject project, QualifierId qualifierId)
        {
            try
            {
                // Create command line and inputs and outputs for pipBuilder.
                if (!TryExecuteArgumentsToPipBuilder(
                    project,
                    qualifierId,
                    out var failureDetail,
                    out var process))
                {
                    Tracing.Logger.Log.SchedulingPipFailure(
                        m_context.LoggingContext,
                        Location.FromFile(project.ProjectFolder.ToString(PathTable)),
                        failureDetail);
                    process = default;

                    return new RushProjectSchedulingFailure(project, failureDetail);
                }

                return process;
            }
            catch (Exception ex)
            {
                Tracing.Logger.Log.UnexpectedPipBuilderException(
                    m_context.LoggingContext,
                    Location.FromFile(project.ProjectFolder.ToString(PathTable)),
                    ex.GetLogEventMessage(),
                    ex.StackTrace);

                return new RushProjectSchedulingFailure(project, ex.ToString());
            }
        }

        private IReadOnlyDictionary<string, string> CreateEnvironment(RushProject project)
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            //
            // Initial environment variables that may be overwritten by the outer environment.
            //

            // Observe there is no need to inform the engine this environment is being used since
            // the same environment was used during graph construction, and the engine is already tracking them
            foreach (var input in m_userDefinedEnvironment)
            {
                string envVarName = input.Key;

                // Temp directory entries are added at pip creation time.
                if (string.Equals(envVarName, "TEMP", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(envVarName, "TMP", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                env[envVarName] = input.Value;
            }

            // node_modules/.bin is expected to be part of the project path. This is standard for JavaScript projects.
            string nodeModulesBin = project.ProjectFolder.Combine(PathTable, RelativePath.Create(PathTable.StringTable, "node_modules/.bin")).ToString(PathTable);
            env["PATH"] = nodeModulesBin + (env.ContainsKey("PATH")? $";{env["PATH"]}" : string.Empty);
            // redirect the user profile so it points under the temp folder
            // use a different path for each build command, since there are tools that happen to generate the same file for, let's say, build and test
            // and we want to avoid double writes as much as possible
            env["USERPROFILE"] = UserProfile(project, PathTable).ToString(PathTable);
            
            return env;
        }

        private bool TryExecuteArgumentsToPipBuilder(
            RushProject project,
            QualifierId qualifierId,
            out string failureDetail,
            out Process process)
        {
            // We create a pip construction helper for each project
            var pipConstructionHelper = GetPipConstructionHelperForProject(project, qualifierId);

            using (var processBuilder = ProcessBuilder.Create(PathTable, m_context.GetPipDataBuilder()))
            {
                // Configure the process to add an assortment of settings: arguments, response file, etc.
                ConfigureProcessBuilder(processBuilder, project);

                // Process all predicted outputs and inputs, including the predicted project dependencies
                ProcessInputs(project, processBuilder);
                ProcessOutputs(project, processBuilder);

                // Try to schedule the process pip
                if (!pipConstructionHelper.TryAddProcess(processBuilder, out ProcessOutputs outputs, out process))
                {
                    failureDetail = "Failed to schedule the pip";
                    return false;
                }

                m_processOutputsPerProject[project] = outputs;

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
        private void ProcessInputs(
            RushProject project,
            ProcessBuilder processBuilder)
        {
            // Add all explicitly declared source files
            foreach (AbsolutePath sourceFile in project.SourceFiles)
            {
                processBuilder.AddInputFile(FileArtifact.CreateSourceFile(sourceFile));
            }

            // Add package.json, which should always be present at the root of the project
            processBuilder.AddInputFile(FileArtifact.CreateSourceFile(project.PackageJsonFile(PathTable)));

            // If dependencies should be tracked via the project-level shrinkwrap-deps file, then force an input
            // dependency on it
            if (m_resolverSettings.TrackDependenciesWithShrinkwrapDepsFile == true)
            {
                processBuilder.AddInputFile(FileArtifact.CreateSourceFile(project.ShrinkwrapDepsFile(PathTable)));
            }

            // In this case all the transitive closure is automatically exposed to the project as direct references. This is standard for
            // JavaScript projects.
            var transitiveReferences = new HashSet<RushProject>();
            ComputeTransitiveDependenciesFor(project, transitiveReferences);
            IEnumerable<RushProject> references = transitiveReferences;

            foreach (RushProject projectReference in references)
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
                    .Where(fa => !fa.Path.IsWithin(m_context.PathTable, LogDirectoryBase(m_frontEndHost.Configuration, m_context.PathTable))))
                {
                    processBuilder.AddInputFile(output);
                }
            }
        }

        private void ProcessOutputs(RushProject project, ProcessBuilder processBuilder)
        {
            // Each project is automatically allowed to write anything under its project root
            processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(project.ProjectFolder), SealDirectoryKind.SharedOpaque);

            // There shouldn't be any writes under node_modules. So exclude it explicitly, since that also avoids a usually expensive enumeration
            // under node_modules when scrubbing. 
            processBuilder.AddOutputDirectoryExclusion(project.NodeModulesFolder(m_context.PathTable));

            // Some projects share their temp folder across their build scripts (e.g. build and test)
            // So we cannot make them share the temp folder with the infrastructure we have today
            // (even though not impossible to fix, we could allow temp directories among pips that are part
            // of the same depedency chain and eagerly delete the folder every time a pip finishes)
            processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(project.TempFolder), SealDirectoryKind.SharedOpaque);

            // This makes sure the folder the user profile is pointing to gets actually created
            processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(UserProfile(project, PathTable)), SealDirectoryKind.SharedOpaque);

            // Add all the additional output directories that the rush graph knows about
            foreach (var outputDirectory in project.OutputDirectories)
            {
                processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(outputDirectory), SealDirectoryKind.SharedOpaque);
            }

            // Add additional output directories configured in the main config file
            AddAdditionalOutputDirectories(processBuilder, project.ProjectFolder);
        }

        private void ComputeTransitiveDependenciesFor(RushProject project, HashSet<RushProject> accumulatedDependencies)
        {
            // We already computed the transitive dependencies for the required project
            if (m_transitiveDependenciesPerProject.TryGetValue(project, out var transitiveDependencies))
            {
                accumulatedDependencies.AddRange(transitiveDependencies);
                return;
            }

            foreach (RushProject dependency in project.Dependencies)
            {
                accumulatedDependencies.Add(dependency);
                ComputeTransitiveDependenciesFor(dependency, accumulatedDependencies);
            }

            m_transitiveDependenciesPerProject.TryAdd(project, accumulatedDependencies.ToReadOnlySet());
        }

        private void ConfigureProcessBuilder(
            ProcessBuilder processBuilder,
            RushProject project)
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

            // By default the double write policy is to allow same content double writes.
            processBuilder.DoubleWritePolicy |= DoubleWritePolicy.AllowSameContentDoubleWrites;

            // Untrack the user profile. The corresponding mount is already configured for not tracking source files, and with allowed undeclared source reads,
            // any attempt to read into the user profile will fail to compute its corresponding hash
            processBuilder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.UserProfile)));

            // If dependencies are tracked with the shrinkwrap-deps file, then untrack everything under the Rush common temp folder, where all package
            // dependencies are placed
            if (m_resolverSettings.TrackDependenciesWithShrinkwrapDepsFile == true)
            {
                processBuilder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(m_rushConfiguration.CommonTempFolder));
            }

            // Add the associated build script name as a tag, so filtering on 'build' or 'test' can happen
            processBuilder.Tags = ReadOnlyArray<StringId>.FromWithoutCopy(new[] { StringId.Create(m_context.StringTable, project.ScriptCommandName) });

            PipConstructionUtilities.UntrackUserConfigurableArtifacts(processBuilder, m_resolverSettings);

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
                    if (m_customCommands.TryGetValue(project.ScriptCommandName, out IReadOnlyList<RushArgument> extraArguments))
                    {
                        foreach (RushArgument value in extraArguments)
                        {
                            AddRushArgumentToBuilder(processBuilder.ArgumentsBuilder, value);
                        }
                    }
                }
            }

            FrontEndUtilities.SetProcessEnvironmentVariables(CreateEnvironment(project), m_userDefinedPassthroughVariables, processBuilder, m_context.PathTable);
        }

        private void AddRushArgumentToBuilder(PipDataBuilder argumentsBuilder, RushArgument value)
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
                    Contract.Assert(false, $"Unexpected rush argument '{value.GetType()}'");
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

        private AbsolutePath GetLogDirectory(RushProject projectFile)
        {
            var success = Root.TryGetRelative(PathTable, projectFile.ProjectFolder, out var inFolderPathFromEnlistmentRoot);
            Contract.Assert(success, $"Configuration root '{Root.ToString(PathTable)}' should be a parent of '{projectFile.ProjectFolder.ToString(PathTable)}'");

            // We hardcode the log to go under the output directory Logs/Rush (and follow the project structure underneath)
            // The 'official' log directory (defined by Configuration.Logging) is not stable in CloudBuild across machines, and therefore it would
            // introduce cache misses
            var result = LogDirectoryBase(m_frontEndHost.Configuration, m_context.PathTable)
                .Combine(PathTable, inFolderPathFromEnlistmentRoot)
                .Combine(PathTable, PipConstructionUtilities.SanitizeStringForSymbol(projectFile.Name))
                .Combine(PathTable, PipConstructionUtilities.SanitizeStringForSymbol(projectFile.ScriptCommandName));

            return result;
        }

        private void SetCmdTool(
            ProcessBuilder processBuilder,
            RushProject project)
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

        private PipConstructionHelper GetPipConstructionHelperForProject(RushProject project, QualifierId qualifierId)
        {
            var pathToProject = project.ProjectFolder;

            // We might be adding the same spec file pip more than once when the same project is evaluated
            // under different global properties, but that's fine, the pip graph ignores duplicates
            m_frontEndHost.PipGraph?.AddSpecFile(
                new SpecFilePip(
                    FileArtifact.CreateSourceFile(pathToProject),
                    new LocationData(pathToProject, 0, 0),
                    m_moduleDefinition.Descriptor.Id));

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
        public void NotifyProjectNotScheduled(RushProject project)
        {
            // TODO: add logging
        }
    }
}
