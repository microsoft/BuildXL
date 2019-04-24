// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Filters pips by module name.
    /// </summary>
    public sealed class ModuleFilter : PipFilter
    {
        private readonly HashSet<StringId> m_modules;

        /// <summary>
        /// Creates a new instance of <see cref="ModuleFilter"/>.
        /// </summary>
        public ModuleFilter(params StringId[] modules)
        {
            m_modules = new HashSet<StringId>(modules);
        }

        /// <inheritdoc/>
        public override void AddStatistics(ref FilterStatistics statistics)
        {
            statistics.ModuleFilterCount++;
        }

        /// <inheritdoc/>
        protected override int GetDerivedSpecificHashCode()
        {
            var hashCode = 0;
            foreach (var module in m_modules)
            {
                hashCode ^= module.GetHashCode();
            }

            return hashCode;
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            ModuleFilter moduleFilter;
            return (moduleFilter = pipFilter as ModuleFilter) != null && m_modules.SetEquals(moduleFilter.m_modules);
        }

        /// <inheritdoc/>
        public override IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(IPipFilterContext context, bool negate = false, IList<PipId> constrainingPips = null)
        {
            var matchingModulePipIds = ParallelProcessAllOutputs<PipId>(
                context,
                (pipId, localPips) =>
                {
                    if (context.GetPipType(pipId) == PipType.Module)
                    {
                        var modulePip = (ModulePip)context.HydratePip(pipId);
                        if (m_modules.Contains(modulePip.Identity))
                        {
                            localPips.Add(pipId);
                        }
                    }
                });

            var dependenciesWithOutputs = GetDependenciesWithOutputsForModulePips(context, matchingModulePipIds);

            if (constrainingPips != null)
            {
                dependenciesWithOutputs.IntersectWith(constrainingPips);
            }

            return ParallelProcessAllOutputs<FileOrDirectoryArtifact>(
                context,
                (pipId, localOutputs) => AddOutputs(context, pipId, localOutputs),
                dependenciesWithOutputs.ToArray());
        }

        /// <inheritdoc/>
        public override IEnumerable<StringId> GetModulesToResolve(bool negate = false)
        {
            return negate ? null : m_modules.ToArray();
        }

        /// <inheritdoc/>
        public override UnionFilterKind UnionFilterKind => UnionFilterKind.Modules;

        /// <inheritdoc/>
        public override PipFilter Union(IEnumerable<PipFilter> filters)
        {
            Contract.Assume(Contract.ForAll(filters, filter => filter is ModuleFilter));
            return new ModuleFilter(filters.Cast<ModuleFilter>().SelectMany(f => f.m_modules).ToArray());
        }
    }
}
