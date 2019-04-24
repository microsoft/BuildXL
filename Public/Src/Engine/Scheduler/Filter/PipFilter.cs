// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Filters pips
    /// </summary>
    public abstract class PipFilter
    {
        /// <summary>
        /// Constructor
        /// </summary>
        protected internal PipFilter()
        {
        }

        /// <summary>
        /// Gets the filter kind indicating filters which can be unioned
        /// </summary>
        public virtual UnionFilterKind UnionFilterKind => UnionFilterKind.None;

        /// <summary>
        /// Combines the given filters
        /// </summary>
        public virtual PipFilter Union(IEnumerable<PipFilter> filters)
        {
            throw Contract.AssertFailure("This should not be called on filters which are not combinable.");
        }

        /// <summary>
        /// Obtains a filter that represents the negation of this filter
        /// </summary>
        public virtual PipFilter Negate()
        {
            return new NegatingFilter(this);
        }

        /// <summary>
        /// Returns the set of output files or directories that match the filter.
        /// </summary>
        /// <remarks>
        /// NOTE: ALL FILTER OPERATION MUST BE IDEMPOTENT BECAUSE THE RESULTS ARE CACHED.
        /// </remarks>
        public IReadOnlySet<FileOrDirectoryArtifact> FilterOutputs(
            IPipFilterContext context,
            bool negate = false,
            IList<PipId> constrainingPips = null)
        {
            if (negate || constrainingPips != null)
            {
                return FilterOutputsCore(context, negate, constrainingPips);
            }

            if (context.TryGetCachedOutputs(this, out IReadOnlySet<FileOrDirectoryArtifact> result))
            {
                return result;
            }

            result = FilterOutputsCore(context);
            context.CacheOutputs(this, result);

            return result;
        }

        /// <summary>
        /// To be implemented by specific filter types. Returns the set of output files or directories that match the filter.
        /// </summary>
        /// <returns>Collection of output files that match the filter</returns>
        public abstract IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(
            IPipFilterContext context,
            bool negate = false,
            IList<PipId> constrainingPips = null);

        /// <summary>
        /// Returns the set of values that must be evaluated to satisfy the filter. This should return null if
        /// all values must be resolved to correctly evaluate the filter.
        /// </summary>
        public virtual IEnumerable<FullSymbol> GetValuesToResolve(bool negate = false)
        {
            return null;
        }

        /// <summary>
        /// Returns the set of roots that must have their values resolved. This should return null if all
        /// value roots must be resolved to correctly evaluate the filter
        /// </summary>
        public virtual IEnumerable<AbsolutePath> GetSpecRootsToResolve(bool negate = false)
        {
            return null;
        }

        /// <summary>
        /// Returns the set of module names that must be evaluated to satisfy the filter. This should return null if all
        /// modules must be resolved to correctly evaluate this filter.
        /// </summary>
        public virtual IEnumerable<StringId> GetModulesToResolve(bool negate = false)
        {
            return null;
        }

        /// <summary>
        /// Collects statistics for all (nested) filters
        /// </summary>
        public abstract void AddStatistics(ref FilterStatistics statistics);

        /// <summary>
        /// Gets the hash code for the derived specific filter members.
        /// </summary>
        protected abstract int GetDerivedSpecificHashCode();

        /// <summary>
        /// Checks if this filter is canonically equal to the given one.
        /// </summary>
        public abstract bool CanonicallyEquals(PipFilter pipFilter);

        /// <summary>
        /// Produces a new canonicalized filter.
        /// </summary>
        public virtual PipFilter Canonicalize(FilterCanonicalizer canonicalizer)
        {
            return canonicalizer.GetOrAdd(this);
        }

        /// <summary>
        /// Gets the pips that can have outputs which are direct dependencies of values of the given spec file pips.
        /// </summary>
        protected static HashSet<PipId> GetDependenciesWithOutputsForModulePips(IPipFilterContext context, IEnumerable<PipId> modulePipIds)
        {
            HashSet<PipId> specFileDependencies = new HashSet<PipId>();
            foreach (var modulePipId in modulePipIds)
            {
                foreach (var dependency in context.GetDependencies(modulePipId))
                {
                    if (context.GetPipType(dependency) == PipType.SpecFile)
                    {
                        specFileDependencies.Add(dependency);
                    }
                }
            }

            return GetDependenciesWithOutputsForSpecFilePips(context, specFileDependencies);
        }

        /// <summary>
        /// Gets the pips that can have outputs which are direct dependencies of values of the given spec file pips.
        /// </summary>
        protected static HashSet<PipId> GetDependenciesWithOutputsForSpecFilePips(IPipFilterContext context, IEnumerable<PipId> specFilePipIds)
        {
            HashSet<PipId> valueDependencies = new HashSet<PipId>();
            foreach (var specFilePipId in specFilePipIds)
            {
                foreach (var dependency in context.GetDependencies(specFilePipId))
                {
                    if (context.GetPipType(dependency) == PipType.Value)
                    {
                        valueDependencies.Add(dependency);
                    }
                }
            }

            return GetDependenciesWithOutputsForValuePips(context, valueDependencies);
        }

        /// <summary>
        /// Gets the pips that can have outputs which are direct dependencies of the given value pips.
        /// </summary>
        protected static HashSet<PipId> GetDependenciesWithOutputsForValuePips(IPipFilterContext context, IEnumerable<PipId> valuePipIds)
        {
            HashSet<PipId> dependenciesWithOutputs = new HashSet<PipId>();
            foreach (var valuePipId in valuePipIds)
            {
                foreach (var dependency in context.GetDependencies(valuePipId))
                {
                    if (MayHaveOutputs(context, dependency))
                    {
                        dependenciesWithOutputs.Add(dependency);
                    }
                }
            }

            return dependenciesWithOutputs;
        }

        /// <summary>
        /// Gets the pips that can have outputs in the transitive closure of a given set of value and seal directory pips
        /// </summary>
        protected static void AddTransitiveSpecDependencies(IPipFilterContext context, HashSet<PipId> specFileIds)
        {
            HashSet<PipId> closure = new HashSet<PipId>();
            var stack = new Stack<PipId>();
            foreach (var rootPipId in specFileIds)
            {
                closure.Add(rootPipId);
                stack.Push(rootPipId);
            }

            while (stack.Count > 0)
            {
                var pipId = stack.Pop();
                foreach (PipId dependency in context.GetDependencies(pipId))
                {
                    if (closure.Add(dependency))
                    {
                        stack.Push(dependency);
                    }
                }

                var pipType = context.GetPipType(pipId);
                if (pipType == PipType.Value)
                {
                    // For value pips, get the spec corresponding spec file pip
                    foreach (var dependent in context.GetDependents(pipId))
                    {
                        if (context.GetPipType(dependent) == PipType.SpecFile)
                        {
                            if (specFileIds.Add(dependent))
                            {
                                stack.Push(dependent);
                            }

                            break;
                        }
                    }
                }
                else if (!pipType.IsMetaPip())
                {
                    // Get the value pip for the pip and push to visit its dependencies and the corresponding spec file
                    foreach (var dependent in context.GetDependents(pipId))
                    {
                        if (context.GetPipType(dependent) == PipType.Value)
                        {
                            if (closure.Add(dependent))
                            {
                                stack.Push(dependent);
                            }

                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets the transitive dependent pips that can have outputs
        /// </summary>
        protected static HashSet<PipId> GetClosureWithOutputs(
            IPipFilterContext context,
            HashSet<PipId> pipIds,
            Func<IPipFilterContext, PipId, IEnumerable<PipId>> getPips,
            ClosureMode closureMode)
        {
            HashSet<PipId> closure = new HashSet<PipId>();

            if (closureMode == ClosureMode.TransitiveIncludingSelf)
            {
                var stack = new Stack<PipId>();
                foreach (var rootPipId in pipIds)
                {
                    stack.Push(rootPipId);
                }

                while (stack.Count > 0)
                {
                    var pipId = stack.Pop();
                    foreach (PipId neighbor in getPips(context, pipId))
                    {
                        if (MayHaveOutputs(context, neighbor) && closure.Add(neighbor))
                        {
                            stack.Push(neighbor);
                        }
                    }
                }
            }
            else
            {
                Contract.Assert(closureMode == ClosureMode.DirectExcludingSelf);
                foreach (var pipId in pipIds)
                {
                    foreach (PipId neighbor in getPips(context, pipId))
                    {
                        if (MayHaveOutputs(context, neighbor))
                        {
                            closure.Add(neighbor);
                        }
                    }
                }
            }

            return closure;
        }

        /// <summary>
        /// Gets the pips that can have outputs in the transitive closure of a given set of value and seal directory pips
        /// </summary>
        protected static HashSet<PipId> GetDependenciesWithOutputsBehindValueAndSealDirectoryPips(IPipFilterContext context, IReadOnlySet<PipId> rootPipIds)
        {
            HashSet<PipId> closure = new HashSet<PipId>();
            HashSet<PipId> dependenciesWithOutputs = new HashSet<PipId>();
            var q = new Queue<PipId>();
            foreach (var rootPipId in rootPipIds)
            {
                closure.Add(rootPipId);
                q.Enqueue(rootPipId);
            }

            while (q.Count > 0)
            {
                var pipId = q.Dequeue();
                foreach (PipId dependency in context.GetDependencies(pipId))
                {
                    switch (context.GetPipType(dependency))
                    {
                        case PipType.Value:
                        case PipType.SealDirectory:
                            if (closure.Add(dependency))
                            {
                                q.Enqueue(dependency);
                            }

                            break;

                        case PipType.WriteFile:
                        case PipType.CopyFile:
                        case PipType.Process:
                        case PipType.Ipc:
                            dependenciesWithOutputs.Add(dependency);
                            break;
                    }
                }
            }

            return dependenciesWithOutputs;
        }

        /// <summary>
        /// Determines whether a pip could have outputs
        /// </summary>
        protected static bool MayHaveOutputs(IPipFilterContext context, PipId pipId)
        {
            switch (context.GetPipType(pipId))
            {
                case PipType.Process:
                case PipType.CopyFile:
                case PipType.SealDirectory:
                case PipType.WriteFile:
                case PipType.Ipc:
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Adds the output files of the pip to the set
        /// </summary>
        protected static void AddOutputs(IPipFilterContext context, PipId pipId, HashSet<FileOrDirectoryArtifact> outputs)
        {
            if (MayHaveOutputs(context, pipId))
            {
                AddOutputs(context.HydratePip(pipId), outputs);
            }
        }

        /// <summary>
        /// Adds the output files of the pip to the set
        /// </summary>
        protected static void AddOutputs(Pip pip, HashSet<FileOrDirectoryArtifact> outputs)
        {
            ForEachOutput(outputs, pip, (outputs2, output) => outputs2.Add(output));
        }

        /// <summary>
        /// Hydrates the pip and calls an action for each of the pip's outputs
        /// (<see cref="ForEachOutput{TState}(TState,IPipFilterContext,PipId,Action{TState,FileOrDirectoryArtifact})"/>.
        /// </summary>
        protected static void ForEachOutput<TState>(TState state, IPipFilterContext context, PipId pipId, Action<TState, FileOrDirectoryArtifact> outputAction)
        {
            ForEachOutput(state, context.HydratePip(pipId), outputAction);
        }

        /// <summary>
        /// Gets the outputs produced by a pip and calls an action
        /// </summary>
        protected static void ForEachOutput<TState>(TState state, Pip pip, Action<TState, FileOrDirectoryArtifact> outputAction)
        {
            switch (pip.PipType)
            {
                case PipType.CopyFile:
                    CopyFile copyFile = (CopyFile)pip;
                    outputAction(state, FileOrDirectoryArtifact.Create(copyFile.Destination));
                    break;
                case PipType.Process:
                    Process process = (Process)pip;
                    foreach (var output in process.FileOutputs)
                    {
                        outputAction(state, FileOrDirectoryArtifact.Create(output.ToFileArtifact()));
                    }

                    foreach (var output in process.DirectoryOutputs)
                    {
                        outputAction(state, FileOrDirectoryArtifact.Create(output));
                    }

                    break;
                case PipType.WriteFile:
                    WriteFile writeFile = (WriteFile)pip;
                    outputAction(state, FileOrDirectoryArtifact.Create(writeFile.Destination));
                    break;
                case PipType.SealDirectory:
                    SealDirectory sealDirectory = (SealDirectory)pip;
                    outputAction(state, FileOrDirectoryArtifact.Create(sealDirectory.Directory));
                    break;
                case PipType.Ipc:
                    IpcPip ipcPip = (IpcPip)pip;
                    outputAction(state, FileOrDirectoryArtifact.Create(ipcPip.OutputFile));
                    break;
            }
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return GetDerivedSpecificHashCode();
        }

        protected static ReadOnlyHashSet<T> ParallelProcessAllOutputs<T>(
            IPipFilterContext context,
            Action<PipId, HashSet<T>> action,
            IList<PipId> pips = null)
        {
            var outputs = new ReadOnlyHashSet<T>();

            pips = pips ?? context.AllPips;

            // Note: pips better be an IList<...> in order to get good Parallel.ForEach performance
            Parallel.ForEach(
                pips,
                () => new HashSet<T>(),
                (pipId, loopState, index, localOutputs) =>
                {
                    action(pipId, localOutputs);
                    return localOutputs;
                },
                localOutputs =>
                {
                    if (localOutputs.Count > 0)
                    {
                        lock (outputs)
                        {
                            // Even though the 'outputs' variable is of type ReadOnlyHashSet
                            // we still can modify it.
                            outputs.UnionWith(localOutputs);
                        }
                    }
                });

            return outputs;
        }
    }

    /// <summary>
    /// The context in which a filter is applied
    /// </summary>
    public interface IPipFilterContext
    {
        /// <summary>
        /// The path table
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        PathTable PathTable { get; }

        /// <summary>
        /// All known pips
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        IList<PipId> AllPips { get; }

        /// <summary>
        /// Materializes pip (expensive)
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        Pip HydratePip(PipId pipId);

        /// <summary>
        /// Obtains the type of a pip
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        PipType GetPipType(PipId pipId);

        /// <summary>
        /// Get the semi stable hash of a pip
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        long GetSemiStableHash(PipId pipId);

        /// <summary>
        /// Gets all dependencies of a pip
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        IEnumerable<PipId> GetDependencies(PipId pipId);

        /// <summary>
        /// Gets all dependents of a pip
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        IEnumerable<PipId> GetDependents(PipId pipId);

        /// <summary>
        /// Gets the producer for the file or directory
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        PipId GetProducer(in FileOrDirectoryArtifact fileOrDirectory);

        /// <summary>
        /// Gets cached filtered outputs.
        /// </summary>
        bool TryGetCachedOutputs(PipFilter pipFilter, out IReadOnlySet<FileOrDirectoryArtifact> outputs);

        /// <summary>
        /// Caches filtered outputs.
        /// </summary>
        void CacheOutputs(PipFilter pipFilter, IReadOnlySet<FileOrDirectoryArtifact> outputs);
    }

}
