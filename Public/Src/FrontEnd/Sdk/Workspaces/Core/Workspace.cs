// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// A workspace represents a set of parsed modules.
    /// </summary>
    /// <remarks>
    /// A workspace may also contain failures that occurred during parsing.
    /// </remarks>
    public class Workspace : BuildXL.FrontEnd.Sdk.Workspaces.IWorkspace
    {
        private readonly ISourceFile[] m_specArray;

        // Spec sources
        private Dictionary<AbsolutePath, SpecFileWithMetadata> m_specSources;

        // Prelude and configuration files
        private readonly Dictionary<AbsolutePath, SpecFileWithMetadata> m_specialSpecs;

        private readonly Dictionary<ModuleDescriptor, ParsedModule> m_allModulesByDescriptor;

        /// <summary>
        /// List of spec modules excluding <see cref="PreludeModule"/> and <see cref="ConfigurationModule"/>.
        /// </summary>
        private IReadOnlyCollection<ParsedModule> m_specModules;

        private IReadOnlyCollection<ParsedModule> m_allModules;

        /// <nodoc/>
        [NotNull]
        public WorkspaceConfiguration WorkspaceConfiguration { get; }

        /// <summary>
        /// All modules with parsed specification files (i.e. excluding <see cref="PreludeModule"/> and <see cref="ConfigurationModule"/>.).
        /// </summary>
        public IReadOnlyCollection<ParsedModule> SpecModules => m_specModules;

        /// <summary>
        /// All modules including prelude and configuration.
        /// </summary>
        public IReadOnlyCollection<ParsedModule> Modules => m_allModules ?? (m_allModules = m_specModules.Union(SpecialModules(PreludeModule, ConfigurationModule)).ToList());

        /// <summary>
        /// Returns the module that was designated as the prelude module, if a prelude was specified. Null otherwise.
        /// </summary>
        [CanBeNull]
        public ParsedModule PreludeModule { get; }

        /// <summary>
        /// Returns a special module that contains all configuration files in parsed form.
        /// </summary>
        [CanBeNull]
        public ParsedModule ConfigurationModule { get; }

        /// <summary>
        /// If true, then the filter was already applied during construction of the workspace.
        /// </summary>
        public bool FilterWasApplied { get; internal set; }

        /// <summary>
        /// If true, then the checker will save file-2-file map.
        /// </summary>
        /// <remarks>
        /// Filtered workspace is constructed based on the existing file-2-file information.
        /// This means that the file-2-file information is stays the same and will be reused from the previous run.
        /// </remarks>
        public bool TrackFileToFileDependencies => !FilterWasApplied && WorkspaceConfiguration.TrackFileToFileDependencies;

        /// <summary>
        /// A map from an <see cref="AbsolutePath"/> to a parsed source file that excludes prelude and configuration files.
        /// </summary>
        [NotNull]
        public IReadOnlyDictionary<AbsolutePath, SpecFileWithMetadata> SpecSources => m_specSources;

        /// <summary>
        /// Returns all the source files from the workspace, including source files for prelude and configuration modules.
        /// </summary>
        public ISourceFile[] GetAllSourceFiles() => m_specArray;

        /// <nodoc/>
        public int SpecCount => m_specSources.Count;

        /// <summary>
        /// Number of all source files including prelude and module configuration files.
        /// </summary>
        public int AllSpecCount => m_specSources.Count + m_specialSpecs.Count;

        /// <nodoc/>
        public int ModuleCount => SpecModules.Count;

        /// <nodoc/>
        public bool IsCanceled => WorkspaceConfiguration.CancellationToken.IsCancellationRequested;

        /// <summary>
        /// <see cref="IWorkspaceProvider"/> that was used to construct the workspace.
        /// </summary>
        [CanBeNull]
        public IWorkspaceProvider WorkspaceProvider { get; }

        /// <nodoc/>
        [NotNull]
        public IReadOnlyCollection<Failure> Failures { get; }

        /// <summary>
        /// Workspace creation succeeded when there are no failures during parsing and parsing was not cancelled.
        /// </summary>
        public bool Succeeded => Failures.Count == 0;

        /// <nodoc/>
        public Workspace(
            [CanBeNull]IWorkspaceProvider provider,
            WorkspaceConfiguration workspaceConfiguration,
            IEnumerable<ParsedModule> modules,
            IEnumerable<Failure> failures,
            [CanBeNull] ParsedModule preludeModule,
            [CanBeNull] ParsedModule configurationModule)
        {
            Contract.Requires(workspaceConfiguration != null);
            Contract.Requires(modules != null);
            Contract.Requires(failures != null);
            Contract.RequiresForAll(modules, m => m != null);

            WorkspaceProvider = provider;
            WorkspaceConfiguration = workspaceConfiguration;

            var allModules = GetAllParsedModules(modules, preludeModule, configurationModule);

            m_specModules = allModules.Where(m => m != preludeModule && m != configurationModule).ToArray();

            // Double ownership is not allowed: specs are already validated for double ownership
            m_specSources = CreateSpecsFromModules(m_specModules, allowDoubleOwnership: false);
            m_specialSpecs = CreateSpecsForPreludeAndConfiguration(preludeModule, configurationModule);

            // Spec array contains all the specs for the workspace.
            m_specArray = m_specSources.ToDictionary().AddRange(m_specialSpecs).Select(s => s.Value.SourceFile).ToArray();

            m_allModulesByDescriptor = AllModulesByDescriptor(allModules);

            Failures = failures.ToArray();

            PreludeModule = preludeModule;
            ConfigurationModule = configurationModule;
        }

        /// <summary>
        /// Constructs the workspace with given errors.
        /// </summary>
        public static Workspace Failure(IWorkspaceProvider provider, WorkspaceConfiguration workspaceConfiguration, params Failure[] failures)
        {
            Contract.Requires(failures.Length != 0);
            return new Workspace(provider, workspaceConfiguration, new List<ParsedModule>(), failures, preludeModule: null, configurationModule: null);
        }

        /// <summary>
        /// Constructs the workspace with given errors in the case where not even a workspace provider could be successfully constructed.
        /// </summary>
        public static Workspace Failure(WorkspaceConfiguration workspaceConfiguration, params Failure[] failures)
        {
            Contract.Requires(failures.Length != 0);
            var workspace = new Workspace(
                provider: null,
                workspaceConfiguration: workspaceConfiguration,
                modules: CollectionUtilities.EmptyArray<ParsedModule>(),
                failures: failures,
                preludeModule: null,
                configurationModule: null);

            return workspace;
        }

        /// <summary>
        /// Creates a workspace for configuration processing.
        /// </summary>
        public static Workspace CreateConfigurationWorkspace(WorkspaceConfiguration configuration, ParsedModule configurationModule, ParsedModule preludeModule)
        {
            return new Workspace(
                provider: null,
                workspaceConfiguration: configuration,
                modules: CollectionUtilities.EmptyArray<ParsedModule>(),
                failures: CollectionUtilities.EmptyArray<Failure>(),
                preludeModule: preludeModule,
                configurationModule: configurationModule);
        }

        /// <summary>
        /// Filters current workspace by using only given modules.
        /// </summary>
        /// <remarks>
        /// The workspace instance is mutable to avoid redundant type checking required to obtain <see cref="SemanticWorkspace"/>.
        /// </remarks>
        public void FilterWorkspace(IReadOnlyCollection<ParsedModule> filteredModules)
        {
            Contract.Requires(filteredModules != null);

            m_specModules = filteredModules;
            // Double ownership is not allowed: specs are already validated for double ownership
            var remainingSpecs = CreateSpecsFromModules(m_specModules, allowDoubleOwnership: false);

            var filteredOutSpecs = new HashSet<ISourceFile>();
            foreach (var spec in m_specSources)
            {
                if (!remainingSpecs.ContainsKey(spec.Key))
                {
                    filteredOutSpecs.Add(spec.Value.SourceFile);
                }
            }

            // Need to notify a semantic model that the filter was applied.
            GetSemanticModel()?.FilterWasApplied(filteredOutSpecs);

            m_specSources = remainingSpecs;
        }

        /// <summary>
        /// Constructs workspace instance with computed semantic model.
        /// </summary>
        public Workspace WithSemanticModel(ISemanticModel semanticModel, bool userFilterWasApplied)
        {
            Contract.Requires(semanticModel != null);

            return new SemanticWorkspace(WorkspaceProvider, WorkspaceConfiguration, SpecModules, PreludeModule, ConfigurationModule, semanticModel, Failures)
            {
                FilterWasApplied = userFilterWasApplied,
            };
        }

        /// <summary>
        /// Constructs a workspace instance with extra failures
        /// </summary>
        public virtual Workspace WithExtraFailures(IEnumerable<Failure> failures)
        {
            Contract.Requires(failures != null);
            return new Workspace(WorkspaceProvider, WorkspaceConfiguration, SpecModules, Failures.Union(failures), PreludeModule, ConfigurationModule);
        }

        /// <summary>
        /// Returns all diagnostics that occurred during parsing.
        /// </summary>
        public IEnumerable<Diagnostic> GetAllParsingErrors()
        {
            foreach (var parseError in Failures.OfType<ParsingFailure>())
            {
                foreach (var diagnostic in parseError.ParseDiagnostics)
                {
                    if (diagnostic.Category == DiagnosticCategory.Error)
                    {
                        yield return diagnostic;
                    }
                }
            }
        }

        /// <summary>
        /// Returns all diagnostics that occurred during local binding.
        /// </summary>
        public IEnumerable<Diagnostic> GetAllBindingErrors()
        {
            foreach (var bindingError in Failures.OfType<BindingFailure>())
            {
                foreach (var diagnostic in bindingError.BindingDiagnostics)
                {
                    if (diagnostic.Category == DiagnosticCategory.Error)
                    {
                        yield return diagnostic;
                    }
                }
            }
        }

        /// <summary>
        /// Returns all diagnostics that occurred during parsing and local binding.
        /// </summary>
        public IEnumerable<Diagnostic> GetAllParsingAndBindingErrors()
        {
            return GetAllParsingErrors().Union(GetAllBindingErrors());
        }

        /// <summary>
        /// Provides <see cref="ISemanticModel"/> if workspace supports it and only when workspace is computed successfully.
        /// </summary>
        public virtual ISemanticModel GetSemanticModel()
        {
            return null;
        }

        /// <summary>
        /// Returns whether the spec belongs to a module with implicit reference semantics
        /// </summary>
        public bool SpecBelongsToImplicitSemanticsModule(AbsolutePath pathToSpec)
        {
            Contract.Assert(ContainsSpec(pathToSpec));
            return GetModuleBySpecFileName(pathToSpec).Definition.ResolutionSemantics == NameResolutionSemantics.ImplicitProjectReferences;
        }

        /// <inheritdoc />
        public IReadOnlySet<AbsolutePath> GetSpecFilesWithImplicitNameVisibility()
        {
            return
                m_specSources
                    .Where(kvp => kvp.Value.OwningModule.Definition.ResolutionSemantics == NameResolutionSemantics.ImplicitProjectReferences)
                    .Select(kvp => kvp.Key)
                    .ToReadOnlySet();
        }

        /// <inheritdoc />
        public IReadOnlySet<AbsolutePath> GetAllSpecFiles() => m_specSources.Keys.Union(m_specSources.Keys).ToReadOnlySet();

        /// <nodoc/>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        public ParsedModule GetModuleBySpecFileName(AbsolutePath specPath)
        {
            Contract.Requires(specPath.IsValid);
            var result = TryGetModuleBySpecFileName(specPath);

            if (result == null)
            {
                throw Contract.AssertFailure(I($"Can't find module for '{specPath}'"));
            }

            return result;
        }

        /// <nodoc/>
        [System.Diagnostics.Contracts.Pure]
        [CanBeNull]
        public ParsedModule TryGetModuleBySpecFileName(AbsolutePath specPath)
        {
            Contract.Requires(specPath.IsValid);

            if (m_specSources.TryGetValue(specPath, out SpecFileWithMetadata specWithMetadata))
            {
                return specWithMetadata.OwningModule;
            }

            if (m_specialSpecs.TryGetValue(specPath, out specWithMetadata))
            {
                return specWithMetadata.OwningModule;
            }

            return null;
        }

        /// <nodoc/>
        public bool TryGetModuleByModuleDescriptor(ModuleDescriptor moduleDescriptor, out ParsedModule parsedModule)
        {
            return m_allModulesByDescriptor.TryGetValue(moduleDescriptor, out parsedModule);
        }

        /// <nodoc/>
        public ParsedModule GetModuleByModuleDescriptor(ModuleDescriptor moduleDescriptor)
        {
            return m_allModulesByDescriptor[moduleDescriptor];
        }

        /// <nodoc/>
        [System.Diagnostics.Contracts.Pure]
        public bool ContainsSpec(AbsolutePath specPath)
        {
            Contract.Requires(specPath.IsValid);
            return m_specSources.ContainsKey(specPath) || m_specialSpecs.ContainsKey(specPath);
        }

        /// <nodoc/>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        public ISourceFile GetSourceFile(AbsolutePath specPath)
        {
            Contract.Requires(ContainsSpec(specPath), "ContainsSpec(specPath)");

            TryGetSourceFile(specPath, out var sourceFile);

            if (sourceFile == null)
            {
                throw Contract.AssertFailure(I($"Can't find source file by the given path '{specPath}'"));
            }

            return sourceFile;
        }

        /// <nodoc/>
        public bool TryGetSourceFile(AbsolutePath specPath, out ISourceFile sourceFile)
        {
            Contract.Requires(specPath.IsValid);

            if (m_specSources.TryGetValue(specPath, out var sourceFileWithMetadata))
            {
                sourceFile = sourceFileWithMetadata.SourceFile;
                return true;
            }

            if (m_specialSpecs.TryGetValue(specPath, out sourceFileWithMetadata))
            {
                sourceFile = sourceFileWithMetadata.SourceFile;
                return true;
            }

            sourceFile = null;
            return false;
        }

        private static Dictionary<AbsolutePath, SpecFileWithMetadata> CreateSpecsForPreludeAndConfiguration(ParsedModule preludeModule, ParsedModule configurationModule)
        {
            // The prelude module used for configuration parsing and the prelude module used for regular spec parsing may be the same
            // So we allow double ownership in this case. The first module being processed will win spec ownership. It shouldn't matter which one.
            return CreateSpecsFromModules(SpecialModules(preludeModule, configurationModule), allowDoubleOwnership: true);
        }

        private static List<ParsedModule> SpecialModules(ParsedModule preludeModule, ParsedModule configurationModule)
        {
            var modules = new List<ParsedModule>();
            if (preludeModule != null)
            {
                modules.Add(preludeModule);
            }

            if (configurationModule != null)
            {
                modules.Add(configurationModule);
            }

            return modules;
        }

        private static Dictionary<AbsolutePath, SpecFileWithMetadata> CreateSpecsFromModules(IReadOnlyCollection<ParsedModule> modules, bool allowDoubleOwnership)
        {
            var result = new Dictionary<AbsolutePath, SpecFileWithMetadata>();
            foreach (var m in modules)
            {
                foreach (var spec in m.Specs)
                {
                    var added = result.TryAdd(spec.Key, SpecFileWithMetadata.CreateNew(m, spec.Value));
                    // It shouldn't happen that the spec was already present when we don't allow double ownership (currently we only allow it for configuration parsing)
                    if (!added && !allowDoubleOwnership)
                    {
                        throw Contract.AssertFailure($"Spec with path {spec.Key} from module '{m.Descriptor.Name}' is already owned by module '{result[spec.Key].OwningModule.Descriptor.Name}'.");
                    }
                }
            }

            return result;
        }

        private static Dictionary<ModuleDescriptor, ParsedModule> AllModulesByDescriptor(HashSet<ParsedModule> allModules)
        {
            var result = new Dictionary<ModuleDescriptor, ParsedModule>();
            foreach (var m in allModules)
            {
                if (result.ContainsKey(m.Descriptor))
                {
                    throw Contract.AssertFailure(I($"Module descriptor '{m.Descriptor}' is already presented in the list of all modules."));
                }

                result.Add(m.Descriptor, m);
            }

            return result;
        }

        private static HashSet<ParsedModule> GetAllParsedModules(
            IEnumerable<ParsedModule> specModules,
            ParsedModule preludeModule,
            ParsedModule configurationModule)
        {
            var result = new HashSet<ParsedModule>(specModules);

            if (preludeModule != null)
            {
                result.Add(preludeModule);
            }

            if (configurationModule != null)
            {
                result.Add(configurationModule);
            }

            return result;
        }
    }
}
