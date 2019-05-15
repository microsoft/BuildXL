// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using ProjectWithPredictions = BuildXL.FrontEnd.MsBuild.Serialization.ProjectWithPredictions<BuildXL.Utilities.AbsolutePath>;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Creates a pip graph based on a collection of <see cref="ProjectWithPredictions"/>
    /// </summary>
    public sealed class PipGraphConstructor
    {
        private readonly FrontEndContext m_context;
        private readonly PipConstructor m_pipConstructor;
        private readonly FrontEndHost m_frontEndHost;

        private static readonly ProjectAndTierComparer s_comparer = new ProjectAndTierComparer();

        /// <nodoc/>
        public PipGraphConstructor(
            FrontEndContext context,
            FrontEndHost frontEndHost,
            ModuleDefinition moduleDefinition,
            IMsBuildResolverSettings resolverSettings,
            AbsolutePath pathToMsBuildExe,
            string frontEndName)
        {
            Contract.Requires(context != null);
            Contract.Requires(frontEndHost != null);
            Contract.Requires(moduleDefinition != null);
            Contract.Requires(resolverSettings != null);
            Contract.Requires(pathToMsBuildExe.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(frontEndName));

            m_context = context;
            m_frontEndHost = frontEndHost;
            m_pipConstructor = new PipConstructor(context, frontEndHost, moduleDefinition, resolverSettings, pathToMsBuildExe, frontEndName);
        }

        /// <summary>
        /// Creates a pip graph that corresponds to all the specified projects in the graph
        /// </summary>
        public async Task<bool> TrySchedulePipsForFilesAsync(IReadOnlySet<ProjectWithPredictions> projectsToEvaluate, QualifierId qualifierId)
        {
            Contract.Requires(qualifierId.IsValid);

            if (!TryTopoSortProjectsAndComputeClosure(projectsToEvaluate, out PriorityQueue<(ProjectWithPredictions, int tier)> topoSortedQueue, out IEnumerable<ProjectWithPredictions> cycle))
            {
                var cycleDescription = string.Join(" -> ", cycle.Select(project => project.FullPath.ToString(m_context.PathTable)));
                Tracing.Logger.Log.CycleInBuildTargets(m_context.LoggingContext, cycleDescription);
                return false;
            }

            bool success = true;

            ActionBlock<ProjectWithPredictions> CreateActionBlockForTier()
            {
                return new ActionBlock<ProjectWithPredictions>(
                    project =>
                    {
                        // We only schedule the project if predicted target collection is non-empty
                        if (project.PredictedTargetsToExecute.Targets.Count != 0)
                        {
                            if (!m_pipConstructor.TrySchedulePipForFile(project, qualifierId, out _, out _))
                            {
                                // Error is already logged
                                success = false;
                            }
                        }
                        else
                        {
                            // Just log a verbose message indicating the project was not scheduled
                            Tracing.Logger.Log.ProjectWithEmptyTargetsIsNotScheduled(
                                m_context.LoggingContext,
                                Location.FromFile(project.FullPath.ToString(m_context.PathTable)),
                                project.FullPath.GetName(m_context.PathTable).ToString(m_context.StringTable));
                        }
                    },
                    new ExecutionDataflowBlockOptions
                    {
                        MaxDegreeOfParallelism = m_frontEndHost.FrontEndConfiguration.MaxFrontEndConcurrency()
                    });
            }

            int currentTier = 0;
            ActionBlock<ProjectWithPredictions> perTierParallelPipCreator = CreateActionBlockForTier();
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
                    perTierParallelPipCreator = CreateActionBlockForTier();
                    currentTier++;
                }

                perTierParallelPipCreator.Post(project);
            }

            perTierParallelPipCreator.Complete();
            await perTierParallelPipCreator.Completion;

            return success;
        }

        private static bool TryTopoSortProjectsAndComputeClosure(
            IReadOnlySet<ProjectWithPredictions> filteredProjects,
            out PriorityQueue<(ProjectWithPredictions, int tier)> topoSortedQueue,
            out IEnumerable<ProjectWithPredictions> cycle)
        {
            var visited = new Dictionary<ProjectWithPredictions, int>();  // Value is tier number of dependency.
            var visiting = new HashSet<ProjectWithPredictions>();
            topoSortedQueue = new PriorityQueue<(ProjectWithPredictions, int tier)>(filteredProjects.Count, s_comparer);

            int minTier = 0;

            foreach (ProjectWithPredictions project in filteredProjects)
            {
                var potentialCycle = new Stack<ProjectWithPredictions>();
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
            ProjectWithPredictions project,
            IDictionary<ProjectWithPredictions, int> visited,
            ISet<ProjectWithPredictions> visiting,
            PriorityQueue<(ProjectWithPredictions, int tier)> topoSortedQueue,
            int minTier,
            Stack<ProjectWithPredictions> potentialCycle,
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

            IReadOnlyCollection<ProjectWithPredictions> dependencies = project.ProjectReferences;

            if (dependencies.Count > 0)
            {
                int maxOfChildTiers = 0;
                foreach (ProjectWithPredictions directDependency in dependencies)
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
        private class ProjectAndTierComparer : IComparer<(ProjectWithPredictions, int tier)>
        {
            public int Compare((ProjectWithPredictions, int tier) x, (ProjectWithPredictions, int tier) y)
            {
                return x.tier.CompareTo(y.tier);
            }
        }
    }
}
