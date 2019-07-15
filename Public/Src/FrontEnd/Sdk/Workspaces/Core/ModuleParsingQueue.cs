// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;
using TypeScript.Net.Binding;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Turns module definitions into parsed modules, by parsing and local binding module definition specs.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable", Justification = "CancellationTokenSource could be left without disposing it.")]
    public class ModuleParsingQueue
    {
        private readonly ConcurrentDictionary<ModuleDescriptor, Possible<IWorkspaceModuleResolver>> m_moduleResolvers = new ConcurrentDictionary<ModuleDescriptor, Possible<IWorkspaceModuleResolver>>();

        // Incomplete modules, meaning that some of their specs are still queued to be parsed.
        private readonly ConcurrentDictionary<ModuleDescriptor, ModuleUnderConstruction> m_modulesToBeParsed;

        // Modules which are complete, meaning that all their specs are already parsed (or failed at parsing)
        private readonly ConcurrentDictionary<ModuleDescriptor, ParsedModule> m_modulesAlreadyParsed;

        // The workspace provider to be used for finding spec dependencies
        private readonly WorkspaceProvider m_workspaceProvider;

        // The collector that is used for identifying what is a module reference in a spec
        private readonly IModuleReferenceResolver m_moduleReferenceResolver;

        // A designated prelude module. May be null if there is not one
        [CanBeNull]
        private readonly ModuleDefinition m_designatedPrelude;

        // Special module that has all configuration files.
        private readonly ParsedModule m_configurationModule;

        // Specs to be parsed queue
        private readonly ActionBlock<SpecWithOwningModule> m_parseQueue;

        // Spec to be bound queue
        private readonly ActionBlock<ParsedSpecWithOwningModule> m_bindQueue;

        // Options for the queue
        private readonly ModuleParsingQueueOptions m_queueOptions;

        // Failures found when parsing
        private readonly ConcurrentQueue<Failure> m_failures;

        private readonly CancellationTokenSource m_cancellationTokenSource = new CancellationTokenSource();

        /// <summary>
        /// CancellationToken
        /// </summary>
        protected System.Threading.CancellationToken CancellationToken => m_cancellationTokenSource.Token;

        [NotNull]
        private readonly ParsingOptions m_parsingOptions;

        // Set to true when no more module may come.
        private volatile bool m_enqueingComplete = false;

        private readonly WorkspaceConfiguration m_workspaceConfiguration;

        /// <summary>
        /// Degree of parallelism used by the parsing queue.
        /// </summary>
        protected readonly int DegreeOfParallelism;

        /// <nodoc />
        protected CancellationTokenRegistration cancellationTokenChain;

        /// <summary>
        /// Creates a module parsing queue. The queue options are specified by the provided queueOptions.
        /// </summary>
        public ModuleParsingQueue(
            [NotNull] WorkspaceProvider workspaceProvider,
            WorkspaceConfiguration workspaceConfiguration,
            IModuleReferenceResolver moduleReferenceResolver,
            ModuleDefinition designatedPrelude,
            ParsedModule configurationModule)
            : this(workspaceProvider, workspaceConfiguration, moduleReferenceResolver, designatedPrelude, configurationModule,
                new ConcurrentDictionary<ModuleDescriptor, ParsedModule>(), new ConcurrentQueue<Failure>())
        {
            Contract.Requires(moduleReferenceResolver != null);
        }

        private ModuleParsingQueue(
            WorkspaceProvider workspaceProvider,
            WorkspaceConfiguration workspaceConfiguration,
            IModuleReferenceResolver moduleReferenceResolver,
            ModuleDefinition designatedPrelude,
            ParsedModule configurationModule,
            ConcurrentDictionary<ModuleDescriptor, ParsedModule> alreadyParsedModules,
            ConcurrentQueue<Failure> failures,
            bool preserveTrivias = false)
        {
            Contract.Requires(workspaceProvider != null);
            Contract.Requires(workspaceConfiguration != null);
            Contract.Requires(moduleReferenceResolver != null);
            Contract.Requires(alreadyParsedModules != null, "alreadyParsedModules != null");
            Contract.Requires(failures != null, "failures != null");

            m_modulesToBeParsed = new ConcurrentDictionary<ModuleDescriptor, ModuleUnderConstruction>();
            m_modulesAlreadyParsed = alreadyParsedModules;
            m_failures = failures;

            m_workspaceProvider = workspaceProvider;
            m_moduleReferenceResolver = moduleReferenceResolver;
            m_designatedPrelude = designatedPrelude;
            m_configurationModule = configurationModule;

            m_workspaceConfiguration = workspaceConfiguration;
            m_parsingOptions = workspaceConfiguration.ParsingOptions;

            if (preserveTrivias)
            {
                m_parsingOptions = (m_parsingOptions ?? ParsingOptions.DefaultParsingOptions).WithTrivia(true);
            }

            DegreeOfParallelism = workspaceConfiguration.MaxDegreeOfParallelismForParsing;

            // WARNING: this is extremely subtle.
            // We need to keep a 'registration token' from the chained operation we are doing next to avoid memory leak.
            // The instance of this class stores the reference to key front-end objects, like resolvers,
            // that keeps the entire front-end in memory.
            // CancellationToken.Register registers the call back, that lead to a closure allocation of the current instance.
            // And this means that the lifetime of this instance is coupled to the lifetime of the the workspaceConfiguration.CancellationToken which is global.
            // This means that if we won't dispose the resistration we'll keep the entire front-end in memory for the entire app life time.
            cancellationTokenChain = workspaceConfiguration.CancellationToken.Register(() => m_cancellationTokenSource.Cancel());

            m_queueOptions = new ModuleParsingQueueOptions()
            {
                CancelOnFirstFailure = workspaceConfiguration.CancelOnFirstFailure,
                MaxDegreeOfParallelism = DegreeOfParallelism,
                CancellationToken = CancellationToken,
            };

            m_parseQueue = new ActionBlock<SpecWithOwningModule>(ProcessQueuedItemForParsing, m_queueOptions);

            Action<ParsedSpecWithOwningModule> action = ProcessQueueItemForBinding;
            m_bindQueue = new ActionBlock<ParsedSpecWithOwningModule>(action, m_queueOptions);
        }

        /// <summary>
        /// Creates a special version of the parsing queue required for paring/binding spec files for fingerprint computation.
        /// </summary>
        public static ModuleParsingQueue CraeteFingerprintComputationQueue(
            WorkspaceProvider workspaceProvider,
            WorkspaceConfiguration workspaceConfiguration,
            IModuleReferenceResolver moduleReferenceResolver)
        {
            return new FingerprintComputationParsingQueue(workspaceProvider, workspaceConfiguration, moduleReferenceResolver);
        }

        /// <summary>
        /// Creates a parsing queue for parsing specs in a regular BuildXL invocation.
        /// </summary>
        public static ModuleParsingQueue Create(
            [NotNull]WorkspaceProvider workspaceProvider,
            [NotNull]WorkspaceConfiguration workspaceConfiguration,
            [NotNull]IModuleReferenceResolver moduleReferenceResolver,
            [CanBeNull]ModuleDefinition designatedPrelude,
            [CanBeNull]ParsedModule configurationModule)
        {
            Contract.Requires(workspaceProvider != null);

            return new ModuleParsingQueue(
                workspaceProvider,
                workspaceConfiguration,
                moduleReferenceResolver,
                designatedPrelude,
                configurationModule);
        }

        /// <summary>
        /// Creates a queue that starts with some already parsed module and a pending module under construction
        /// </summary>
        public static ModuleParsingQueue CreateIncrementalQueue(
            WorkspaceProvider workspaceProvider,
            WorkspaceConfiguration workspaceConfiguration,
            IModuleReferenceResolver moduleReferenceResolver,
            ModuleDefinition designatedPrelude,
            ParsedModule configurationModule,
            IEnumerable<ParsedModule> parsedModules,
            IEnumerable<Failure> failures)
        {
            Contract.Requires(workspaceProvider != null);
            Contract.Requires(moduleReferenceResolver != null);
            Contract.Requires(parsedModules != null);
            Contract.Requires(failures != null);

            var parsedModulesDictionary =
                new ConcurrentDictionary<ModuleDescriptor, ParsedModule>(parsedModules.Select(parsedModule => new KeyValuePair<ModuleDescriptor, ParsedModule>(parsedModule.Descriptor, parsedModule)));

            var failureBag = new ConcurrentQueue<Failure>(failures);

            // For IDE mode it is very crucial to preserve trivias. For instance, without it, there is no way to check that the current position is inside a comment.
            var queue = new ModuleParsingQueue(
                workspaceProvider,
                workspaceConfiguration,
                moduleReferenceResolver,
                designatedPrelude,
                configurationModule,
                parsedModulesDictionary,
                failureBag,
                preserveTrivias: true);

            return queue;
        }

        /// <summary>
        /// Process a partially constructed module
        /// </summary>
        public Task<Workspace> ProcessIncrementalAsync(ModuleUnderConstruction moduleUnderConstruction)
        {
            Contract.Requires(moduleUnderConstruction != null);
            Contract.Requires(!moduleUnderConstruction.IsModuleComplete());

            // Add the module to be parsed as is
            var definition = moduleUnderConstruction.Definition;

            Contract.Assert(!m_modulesToBeParsed.ContainsKey(definition.Descriptor));
            m_modulesToBeParsed[definition.Descriptor] = moduleUnderConstruction;

            // Now schedule all the pending specs
            // It is extremely important to get specs in order to preserve determinism.
            var pendingSpecs = moduleUnderConstruction.GetPendingSpecPathsOrderedByPath(m_workspaceProvider.PathTable);
            foreach (var spec in pendingSpecs)
            {
                m_parseQueue.Post(new SpecWithOwningModule(spec.path, definition));
            }

            return EnqueuingFinishedAndWaitForCompletion();
        }

        /// <nodoc />
        public virtual Possible<ISourceFile>[] ParseAndBindSpecs(SpecWithOwningModule[] specs)
        {
            throw new NotImplementedException("Please use 'FingerprintComputationParsingQueue'.");
        }

        /// <nodoc />
        public Task<Workspace> ProcessAsync(IEnumerable<ModuleDefinition> moduleDefinitions)
        {
            Contract.Requires(moduleDefinitions != null);

            // Add all requested modules to the parsing queue
            foreach (var moduleDefinition in moduleDefinitions)
            {
                EnqueueModuleForParsing(moduleDefinition);
            }

            return EnqueuingFinishedAndWaitForCompletion();
        }

        private async Task<Workspace> EnqueuingFinishedAndWaitForCompletion()
        {
            // No more new module definition may be added for processing
            EnqueuingCompleted();

            try
            {
                await m_parseQueue.Completion;
            }
            catch (TaskCanceledException)
            {
                // Expected. This means that at least one task failed parsing
            }

            // Parsing is complete. Just need to wait for pending items to finish binding.
            m_bindQueue.Complete();

            try
            {
                await m_bindQueue.Completion;
            }
            catch (TaskCanceledException)
            {
                // Expected. This means that at least one task failed binding
            }

            // Need to check if the queue reparsed prelude or configuration module.
            ParsedModule prelude = GetProcessedPreludeOrDefault();
            ParsedModule configurationModule = GetProcessedConfigurationModuleOrDefault();

            // The workspace is ready to be constructed. So at this point we can run some workspace-level validations.
            var workspaceFailures = WorkspaceValidator.ValidateParsedModules(m_modulesAlreadyParsed.Values, m_workspaceProvider.PathTable);

            // Create the result. Observe that if the task is cancelled, we don't propagate it
            // but reflect it in the result.
            return new Workspace(
                m_workspaceProvider,
                m_workspaceConfiguration,
                GetAllSourceModules(prelude, configurationModule),
                m_failures.Union(workspaceFailures),
                preludeModule: prelude,
                configurationModule: configurationModule);
        }

        private IEnumerable<ParsedModule> GetAllSourceModules(ParsedModule prelude, ParsedModule configurationModule)
        {
            var result = new HashSet<ParsedModule>(m_modulesAlreadyParsed.Values);

            if (prelude != null)
            {
                result.Remove(prelude);
            }

            if (configurationModule != null)
            {
                result.Remove(configurationModule);
            }

            return result;
        }

        /// <summary>
        /// Enqueues all the specs of <param name="moduleDefinition"/> for parsing. Actual enqueuing only happens if the module has not been
        /// completed yet, nor already scheduled.
        /// </summary>
        /// <remarks>
        /// This method can only be called before calling <see cref="EnqueuingCompleted"/>
        /// </remarks>
        private void EnqueueModuleForParsing(ModuleDefinition moduleDefinition)
        {
            if (m_modulesAlreadyParsed.ContainsKey(moduleDefinition.Descriptor)
                || m_modulesToBeParsed.ContainsKey(moduleDefinition.Descriptor))
            {
                return;
            }

            // If the module has no specs, it is already parsed
            if (moduleDefinition.Specs.Count == 0)
            {
                m_modulesAlreadyParsed[moduleDefinition.Descriptor] = new ParsedModule(moduleDefinition);
                return;
            }

            // Create a module with no specs and add it to the modules-to-be-parsed collection
            var module = new ModuleUnderConstruction(moduleDefinition);
            if (!m_modulesToBeParsed.TryAdd(moduleDefinition.Descriptor, module))
            {
                // If the module has already been added by someone else, we just declare the enqueue successful
                return;
            }

            // Now schedule all the specs
            foreach (var spec in moduleDefinition.Specs)
            {
                // Constructor of this class ensures that queue is unbounded.
                // In cases of bounded queue, posting to a queue can lead to a deadlock
                // because ActionBlock infrastructure removes item from the queue
                // only when call back that processes that element finishes its execution.
                // But in this case, callback can add item to the queue which will lead to a deadlock
                // when queue is full.
                m_parseQueue.Post(new SpecWithOwningModule(spec, moduleDefinition));
            }
        }

        /// <summary>
        /// Notifies that no new modules will be scheduled for parsing
        /// </summary>
        private void EnqueuingCompleted()
        {
            m_enqueingComplete = true;

            // If we are already done parsing everything enqueued so far, we flag the queue as completed.
            // This is very unlikely, but may be possible.
            if (m_modulesToBeParsed.IsEmpty)
            {
                cancellationTokenChain.Dispose();
                m_parseQueue.Complete();
            }
        }

        /// <summary>
        /// Parses a <param name="specWithOwningModule"/> and adds it to the set of modules being constructed.
        /// Additionally, it finds out if the spec has external module dependencies and schedule those for parsing if needed.
        /// </summary>
        private async Task ProcessQueuedItemForParsing(SpecWithOwningModule specWithOwningModule)
        {
            if (!m_modulesToBeParsed.TryGetValue(specWithOwningModule.OwningModule.Descriptor, out ModuleUnderConstruction owningModule))
            {
                // If the module is no longer under construction, that means that somebody else already completed it
                // so we just return
                return;
            }

            try
            {
                // Tries to parse the spec
                var maybeParsedFile = await TryParseSpec(specWithOwningModule);

                ISourceFile parsedFile;
                if (!maybeParsedFile.Succeeded)
                {
                    ReportFailureAndCancelParsingIfNeeded(maybeParsedFile.Failure);

                    // If there is a failure, then two things are possible:
                    // 1) The spec was parsed but it contains parsing errors. In this case we add it to the module anyway
                    // 2) Another type of failure happened (e.g. the module resolver for that spec could not be found).  In this case
                    // we notify that a failure happened so we can keep track of when a module under construction is complete
                    var parsingFailure = maybeParsedFile.Failure as ParsingFailure;
                    if (parsingFailure != null)
                    {
                        parsedFile = parsingFailure.SourceFile;
                    }
                    else
                    {
                        owningModule.ParsingFailed();
                        return;
                    }
                }
                else
                {
                    parsedFile = maybeParsedFile.Result;
                }

                // We enqueue the spec external dependencies and update the parsed file external dependencies
                var enqueueResult = await EnqueueSpecDependenciesIfAny(owningModule, parsedFile);
                if (!enqueueResult.Succeeded)
                {
                    ReportFailureAndCancelParsingIfNeeded(enqueueResult.Failure);
                }

                // Now that we are done parsing the spec, we add it to the module-to-be
                // It is important to add the file to the module after scheduling dependencies, since scheduling
                // also updates the source file external references. Otherwise, some other thread
                // may complete the module and update all its internal references at the same time, which
                // is not thread safe
                var addResult = owningModule.AddParsedSpec(specWithOwningModule.Path, parsedFile);

                if (addResult != ModuleUnderConstruction.AddParsedSpecResult.SpecIsCandidateForInjectingQualifiers)
                {
                    ScheduleSourceFileBinding(new ParsedSpecWithOwningModule(parsedFile: parsedFile, owningModule: specWithOwningModule.OwningModule));
                }
            }
            finally
            {
                // If the module is completed and there are no more modules to complete, we are done
                CheckForCompletionAndSignalQueueIfDone(owningModule);
            }
        }

        private void ScheduleSourceFileBinding(ParsedSpecWithOwningModule parsedSpecWithOwningModule)
        {
            var parsedFile = parsedSpecWithOwningModule.ParsedFile;

            // We can safely schedule binding for the spec.
            // Don't need to bind if parse errors are present and we cancel on failure.
            if (parsedFile.ParseDiagnostics.Count == 0 || !m_queueOptions.CancelOnFirstFailure)
            {
                m_bindQueue.Post(parsedSpecWithOwningModule);
            }
        }

        /// <summary>
        /// Adds the failure to the set of failures and, if early bail out is specified, sends a cancellation
        /// </summary>
        private void ReportFailureAndCancelParsingIfNeeded(params Failure[] failures)
        {
            foreach (var failure in failures)
            {
                m_failures.Enqueue(failure);
            }

            if (m_queueOptions.CancelOnFirstFailure)
            {
                m_cancellationTokenSource.Cancel();
            }
        }

        private void CheckForCompletionAndSignalQueueIfDone(ModuleUnderConstruction owningModule)
        {
            // Now that all dependencies of the owning module have been scheduled,
            // we check if the module for which we just added a spec is complete
            if (owningModule.IsModuleFirstTimeComplete())
            {
                var moduleId = owningModule.Definition.Descriptor;

                // We create a parsed module out of a module under construction.
                // Observe many threads can try to create the same completed module. But that's ok, the method is thread safe
                // and always return the same instance.
                // It is important to notice that we should add this module to m_modulesAlreadyParsed before removing it from
                // m_modulesToBeParsed. This is so no producer will think the module is not there at any point in time (which
                // would cause the producer to try to schedule that module again)
                var success = owningModule.TryCreateCompletedModule(m_moduleReferenceResolver, m_workspaceConfiguration, m_queueOptions.CancelOnFirstFailure, out ParsedModule module, out Failure[] failures);
                if (success)
                {
                    m_modulesAlreadyParsed[moduleId] = module;

                    // Now the module is completed and we can bind the changed file as well.
                    var modifiedSourceFile = owningModule.GetSourceFileForInjectingQualifiers();
                    if (modifiedSourceFile != null)
                    {
                        ScheduleSourceFileBinding(new ParsedSpecWithOwningModule(parsedFile: modifiedSourceFile, owningModule: owningModule.Definition));
                    }
                }
                else
                {
                    // This is a bogus module we put here when failures occurr. This guarantees that modules
                    // are not scheduled more than once (detailed reasons above). When there are failures
                    // we want to report them only once, so leave that to whoever successfully removes
                    // the module from m_modulesToBeParsed to report them.
                    // Observe that this (empty) module will be left in case of failures, but that's not
                    // likely to be a problem
                    m_modulesAlreadyParsed[moduleId] = new ParsedModule(owningModule.Definition, hasFailures: true);
                }

                // If removing fails, that means that somebody else successfully removed the module, so we do nothing
                // If removing succeeds, we are responsible for signaling if the queue is done
                if (m_modulesToBeParsed.TryRemove(moduleId, out ModuleUnderConstruction dummy))
                {
                    if (!success)
                    {
                        ReportFailureAndCancelParsingIfNeeded(failures);
                    }

                    // Now we decide if we are done with parsing the transitive closure.
                    // This happens when the set of modules to be parsed is empty
                    // and EnqueuingCompleted has been called.
                    // Observe that it is important that we first add all dependencies to be parsed
                    // before removing a completed module. This ensures
                    // that if anybody sees an empty collection, we are done for sure
                    if (m_modulesToBeParsed.IsEmpty && m_enqueingComplete)
                    {
                        cancellationTokenChain.Dispose();
                        m_parseQueue.Complete();
                    }
                }
            }
        }

        /// <summary>
        /// Discover external module dependencies and enqueue them if necessary
        /// </summary>
        /// <remarks>
        /// The parsed file gets updated with the external module references as they are discovered.
        /// </remarks>
        private async Task<Possible<bool>> EnqueueSpecDependenciesIfAny(ModuleUnderConstruction owningModule, ISourceFile parsedFile)
        {
            var allSpecifiers = m_moduleReferenceResolver.GetExternalModuleReferences(parsedFile);

            foreach (var moduleName in allSpecifiers)
            {
                // Get the module definition from the resolver and enqueue it. Since we don't deal with versions yet at the
                // import level, there should be exactly one module definition with that name
                var maybeModuleDefinition = await m_workspaceProvider.FindUniqueModuleDefinitionWithName(moduleName);
                if (!maybeModuleDefinition.Succeeded)
                {
                    return maybeModuleDefinition.Failure;
                }

                var moduleDefinition = maybeModuleDefinition.Result;

                // Since the referenced module has been found, we update the parsed file with the reference. This
                // information is later used by the checker.
                if (!m_moduleReferenceResolver.TryUpdateExternalModuleReference(parsedFile, moduleDefinition, out var failure))
                {
                    return failure;
                }

                // Update the owning module advertising that the found module was referenced
                owningModule.AddReferencedModule(moduleDefinition.Descriptor, moduleName.ReferencedFrom);

                EnqueueModuleForParsing(moduleDefinition);
            }

            return true;
        }

        private Possible<IWorkspaceModuleResolver> GetOrFindModuleResolver(AbsolutePath pathToSpec, ModuleDescriptor descriptor)
        {
            if (!m_moduleResolvers.TryGetValue(descriptor, out Possible<IWorkspaceModuleResolver> result))
            {
                result = m_workspaceProvider.FindResolverAsync(pathToSpec).GetAwaiter().GetResult();
                m_moduleResolvers.TryAdd(descriptor, result);
            }

            return result;
        }

        /// <nodoc />
        protected async Task<Possible<ISourceFile>> TryParseSpec(SpecWithOwningModule specWithOwningModule)
        {
            var pathToSpec = specWithOwningModule.Path;

            var maybeResolver = GetOrFindModuleResolver(pathToSpec, specWithOwningModule.OwningModule.Descriptor);

            // TODO: if the spec we are parsing belongs to the prelude, we override the automatic export namespace configuration for that spec to false. This is a temporary hack!
            // Rationale: A top-level export is one of the ways to split internal vs external modules. With this DScript-specific
            // change around automatic namespace export, we've lost the ability to distinguish this as soon as an
            // inner member is exported. This means that the DS prelude is identified as an external module and therefore not merged.
            // A better solution: turn the DScript prelude into a true external module, where all declarations are exported.
            // but merge all those in the checker (where the prelude is nicely identified as such) as globals, so there is no
            // need to explicitly import the prelude anywhere (checked this already, and it works). Not doing it now because
            // that would mean a breaking change in the prelude that the typescript-based type checker won't be able to deal with
            // today.

            // If we're processing the prelude, then we need to use prelude-specific parsing options.
            // withQualifierFunction needs to be generated when the owning module is a V2 one
            // TODO: consider splitting the parsing options into UserConfigurableParsingOptions and ParsingOptions
            // The first one is the one that should be exposed beyond this class
            var parsingOptions = specWithOwningModule.OwningModule.Descriptor == m_designatedPrelude?.Descriptor
                ? ParsingOptions.GetPreludeParsingOptions(m_parsingOptions.EscapeIdentifiers)
                : m_parsingOptions
                    .WithFailOnMissingSemicolons(true)
                    .WithGenerateWithQualifierFunctionForEveryNamespace(specWithOwningModule.OwningModule.ResolutionSemantics == NameResolutionSemantics.ImplicitProjectReferences);

            // Intentionally switched from monadic syntax (with maybe.Then) to avoid additional closure allocations.
            if (!maybeResolver.Succeeded)
            {
                return maybeResolver.Failure;
            }

            var maybeParsedFile = await maybeResolver.Result.TryParseAsync(pathToSpec, specWithOwningModule.OwningModule.ModuleConfigFile, parsingOptions);

            // Check if there are any diagnostics reported by the parser
            if (!maybeParsedFile.Succeeded)
            {
                return maybeParsedFile.Failure;
            }

            var parsedFile = maybeParsedFile.Result;
            return parsedFile.ParseDiagnostics.Count != 0
                ? new ParsingFailure(specWithOwningModule.OwningModuleDescriptor, parsedFile)
                : new Possible<ISourceFile>(parsedFile);
        }

        private ParsedModule GetProcessedPreludeOrDefault()
        {
            // If there is a designated prelude module, we identify it for the workspace
            ParsedModule prelude = null;
            if (m_designatedPrelude != null)
            {
                m_modulesAlreadyParsed.TryGetValue(m_designatedPrelude.Descriptor, out prelude);
            }

            return prelude;
        }

        private ParsedModule GetProcessedConfigurationModuleOrDefault()
        {
            ParsedModule result = m_configurationModule;
            if (m_configurationModule != null)
            {
                if (m_modulesAlreadyParsed.TryGetValue(m_configurationModule.Descriptor, out var parsedConfigurationModule))
                {
                    result = parsedConfigurationModule;
                }
            }

            return result;
        }

        private void ProcessQueueItemForBinding(ParsedSpecWithOwningModule parsedSpecWithOwningModule)
        {
            BindSourceFile(parsedSpecWithOwningModule);

            var parsedFile = parsedSpecWithOwningModule.ParsedFile;
            if (parsedFile.BindDiagnostics.Count != 0)
            {
                ReportFailureAndCancelParsingIfNeeded(
                    new BindingFailure(parsedSpecWithOwningModule.OwningModule.Descriptor, parsedFile));
            }
        }

        /// <nodoc />
        protected void BindSourceFile(ParsedSpecWithOwningModule parsedSpecWithOwningModule)
        {
            var sourceFile = parsedSpecWithOwningModule.ParsedFile;

            // Don't need to bind if the file is already bound (incremental case)
            if (sourceFile.State == SourceFileState.Bound)
            {
                return;
            }

            var specPathString = parsedSpecWithOwningModule.ParsedFile.Path.AbsolutePath;
            using (m_workspaceProvider.Statistics.SpecBinding.Start(specPathString))
            {
                Binder.Bind(sourceFile, CompilerOptions.Empty);
                m_workspaceProvider.Statistics.SourceFileSymbols.Increment(sourceFile.SymbolCount, specPathString);
            }

            // Once binding is done, we can compute the binding fingerprint,
            // that is required for filtered scenarios.
            if (sourceFile.ParseDiagnostics.Count == 0 && sourceFile.BindDiagnostics.Count == 0 && m_workspaceConfiguration.ConstructFingerprintDuringParsing)
            {
                using (m_workspaceProvider.Statistics.SpecComputeFingerprint.Start())
                {
                    sourceFile.ComputeBindingFingerprint(m_workspaceProvider.SymbolTable);
                }
            }
        }
    }
}
