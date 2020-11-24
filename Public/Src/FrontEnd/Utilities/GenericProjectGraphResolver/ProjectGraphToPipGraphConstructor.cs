// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.FrontEnd.MsBuild;
using BuildXL.FrontEnd.Sdk.ProjectGraph;
using BuildXL.Pips.Builders;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver
{
    /// <summary>
    /// Given a collection of projects with dependencies, schedules projects in toposorted order.
    /// </summary>
    public sealed class ProjectGraphToPipGraphConstructor<TProject> where TProject : IProjectWithDependencies<TProject>
    {
        private readonly IProjectToPipConstructor<TProject> m_pipConstructor;
        private readonly int m_maxConcurrency;

        private static readonly ProjectAndTierComparer s_comparer = new ProjectAndTierComparer();

        /// <nodoc/>
        public ProjectGraphToPipGraphConstructor(
            IProjectToPipConstructor<TProject> constructor,
            int maxConcurrency)
        {
            Contract.Requires(constructor != null);

            m_pipConstructor = constructor;
            m_maxConcurrency = maxConcurrency;
        }

        /// <summary>
        /// Creates a pip graph that corresponds to all the specified projects in the graph
        /// </summary>
        public async Task<Possible<ProjectGraphSchedulingResult<TProject>>> TrySchedulePipsForFilesAsync(IReadOnlySet<TProject> projectsToEvaluate, QualifierId qualifierId, Func<ProjectCreationResult<TProject>, Possible<ProcessOutputs>> customScheduler = null)
        {
            Contract.Requires(qualifierId.IsValid);

            if (!TryTopoSortProjectsAndComputeClosure(projectsToEvaluate, out PriorityQueue<(TProject, int tier)> topoSortedQueue, out IEnumerable<TProject> cycle))
            {
                return new Possible<ProjectGraphSchedulingResult<TProject>>(new CycleInProjectsFailure<TProject>(cycle));
            }

            bool success = true;
            // Observe in case of multiple failure we non-deterministically report one
            Failure failure = null;
            var processOutputs = new ConcurrentDictionary<TProject, ProcessOutputs>();

            ActionBlock<TProject> createActionBlockForTier()
            {
                return new ActionBlock<TProject>(
                    project =>
                    {
                        // If a previous tier had any errors (which is guaranteed to be completed before the next tier is scheduled), just return, since the current tier may depend on pips that in the end
                        // were not scheduled
                        if (!success)
                        {
                            return;
                        }

                        // We only schedule the project if the project does not rule itself out from being added to the graph
                        if (project.CanBeScheduled())
                        {
                                var maybeResult = m_pipConstructor.TryCreatePipForProject(project, qualifierId);
                                if (!maybeResult.Succeeded)
                                {
                                    // Error is already logged
                                    success = false;
                                    failure = maybeResult.Failure;
                                    return;
                                }

                            // If a custom scheduler is not present, or it returns null when called, we add the resulting pip as the pip constructor created it
                            if (customScheduler == null || 
                                (customScheduler(maybeResult.Result) is var maybeCustomSchedulingOutputs && maybeCustomSchedulingOutputs.Succeeded && maybeCustomSchedulingOutputs.Result == null))
                            {
                                var maybeOutputs = m_pipConstructor.TrySchedulePipForProject(maybeResult.Result, qualifierId);
                                if (!maybeOutputs.Succeeded)
                                {
                                    // Error is already logged
                                    success = false;
                                    failure = maybeOutputs.Failure;
                                    return;
                                }

                                processOutputs.TryAdd(project, maybeOutputs.Result);
                            }
                            else if (maybeCustomSchedulingOutputs.Succeeded)
                            {
                                // In this case the custom scheduler should have already added the pip. Notify the pip constructor and keep track of the project -> output association
                                m_pipConstructor.NotifyCustomProjectScheduled(project, maybeCustomSchedulingOutputs.Result, qualifierId);
                                processOutputs.TryAdd(project, maybeCustomSchedulingOutputs.Result);
                            }
                            else
                            {
                                // In this case the custom scheduler evaluated to an error
                                success = false;
                                failure = maybeCustomSchedulingOutputs.Failure;
                                return;
                            }
                        }
                        else
                        {
                            m_pipConstructor.NotifyProjectNotScheduled(project);
                        }
                    },
                    new ExecutionDataflowBlockOptions
                    {
                        MaxDegreeOfParallelism = m_maxConcurrency
                    });
            }

            int currentTier = 0;
            ActionBlock<TProject> perTierParallelPipCreator = createActionBlockForTier();
            while (topoSortedQueue.Count != 0 && success)
            {
                if (!success)
                {
                    break;
                }

                var (project, tier) = topoSortedQueue.Top;
                topoSortedQueue.Pop();
                if (tier != currentTier)
                {
                    perTierParallelPipCreator.Complete();
                    await perTierParallelPipCreator.Completion;
                    perTierParallelPipCreator = createActionBlockForTier();
                    currentTier++;
                }

                perTierParallelPipCreator.Post(project);
            }

            perTierParallelPipCreator.Complete();
            await perTierParallelPipCreator.Completion;

            return success? 
                new ProjectGraphSchedulingResult<TProject>(processOutputs) : 
                (Possible<ProjectGraphSchedulingResult<TProject>>) failure;
        }

        private static bool TryTopoSortProjectsAndComputeClosure(
            IReadOnlySet<TProject> filteredProjects,
            out PriorityQueue<(TProject, int tier)> topoSortedQueue,
            out IEnumerable<TProject> cycle)
        {
            var visited = new Dictionary<TProject, int>();  // Value is tier number of dependency.
            var visiting = new HashSet<TProject>();
            topoSortedQueue = new PriorityQueue<(TProject, int tier)>(filteredProjects.Count, s_comparer);

            int minTier = 0;

            foreach (TProject project in filteredProjects)
            {
                var potentialCycle = new Stack<TProject>();
                if (!TryTopoSortProjectAndComputeClosure(project, visited, visiting, topoSortedQueue, minTier, potentialCycle, out _))
                {
                    // The returned stack needs to be reversed so, when enumerating, the traversal goes from dependency to dependent
                    cycle = potentialCycle.Reverse();
                    return false;
                }
            }

            cycle = null;
            return true;
        }

        // Returns in projectTier the tier number of the project.
        // Parents should add 1 to the highest tier from amongst their children.
        private static bool TryTopoSortProjectAndComputeClosure(
            TProject project,
            IDictionary<TProject, int> visited,
            ISet<TProject> visiting,
            PriorityQueue<(TProject, int tier)> topoSortedQueue,
            int minTier,
            Stack<TProject> potentialCycle,
            out int projectTier)
        {
            if (visited.TryGetValue(project, out projectTier))
            {
                // We have already seen this project, all dependencies are already added, but we
                // need to increase the tier number of the parent.
                return true;
            }

            potentialCycle.Push(project);

            if (visiting.Contains(project))
            {
                // cycle! Abort the topo sort
                projectTier = -1;
                return false;
            }

            visiting.Add(project);

            IReadOnlyCollection<TProject> dependencies = project.Dependencies;

            if (dependencies.Count > 0)
            {
                int maxOfChildTiers = 0;
                foreach (TProject directDependency in dependencies)
                {
                    if (!TryTopoSortProjectAndComputeClosure(directDependency, visited, visiting, topoSortedQueue, minTier, potentialCycle, out int childTier))
                    {
                        return false;
                    }
                    maxOfChildTiers = Math.Max(childTier, maxOfChildTiers);
                }

                projectTier = maxOfChildTiers + 1;
            }
            else
            {
                projectTier = minTier;
            }

            potentialCycle.Pop();

            // At this point all dependencies have been added to the topo sorted queue, so it is safe to add the current project.
            topoSortedQueue.Push((project, projectTier));
            visited.Add(project, projectTier);
            visiting.Remove(project);

            return true;
        }

        /// <summary>
        /// Sorts low to high for tier number.
        /// </summary>
        private class ProjectAndTierComparer : IComparer<(TProject, int tier)>
        {
            public int Compare((TProject, int tier) x, (TProject, int tier) y)
            {
                return x.tier.CompareTo(y.tier);
            }
        }
    }
}
