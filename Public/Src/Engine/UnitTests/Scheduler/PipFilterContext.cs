// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Filter;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Test.BuildXL.Scheduler
{
    internal class PipFilterContext : IPipFilterContext
    {
        public PathTable PathTable { get; }
        public IList<PipId> AllPips { get; }

        private readonly Func<PipId, Pip> m_pipHydrator;
        private readonly Func<PipId, IEnumerable<PipId>> m_pipDependenciesGetter;
        private readonly Func<PipId, IEnumerable<PipId>> m_pipDependentsGetter;
        private readonly Func<FileOrDirectoryArtifact, PipId> m_producerGetter;

        private readonly Dictionary<PipFilter, IReadOnlySet<FileOrDirectoryArtifact>> m_cachedOutputs =
            new Dictionary<PipFilter, IReadOnlySet<FileOrDirectoryArtifact>>(new CachedOutputKeyComparer());

        public PipFilterContext(
            PathTable pathTable,
            IList<PipId> allPips,
            Func<PipId, Pip> pipHydrator,
            Func<PipId, IEnumerable<PipId>> pipDependenciesGetter,
            Func<PipId, IEnumerable<PipId>> pipDependentsGetter = null,
            Func<FileOrDirectoryArtifact, PipId> producerGetter = null)
        {
            PathTable = pathTable;
            AllPips = allPips;
            m_pipHydrator = pipHydrator;
            m_pipDependenciesGetter = pipDependenciesGetter;
            m_pipDependentsGetter = pipDependentsGetter;
            m_producerGetter = producerGetter;
        }

        public PipType GetPipType(PipId pipId)
        {
            Contract.Requires(pipId.IsValid);
            return HydratePip(pipId).PipType;
        }

        public long GetSemiStableHash(PipId pipId)
        {
            Contract.Requires(pipId.IsValid);
            return HydratePip(pipId).SemiStableHash;
        }

        public Pip HydratePip(PipId pipId)
        {
            Contract.Requires(pipId.IsValid);
            return m_pipHydrator(pipId);
        }

        public IEnumerable<PipId> GetDependencies(PipId pipId)
        {
            Contract.Requires(pipId.IsValid);
            return m_pipDependenciesGetter(pipId);
        }

        public IEnumerable<PipId> GetDependents(PipId pipId)
        {
            Contract.Requires(pipId.IsValid);
            return m_pipDependentsGetter?.Invoke(pipId) ?? Enumerable.Empty<PipId>();
        }

        public PipId GetProducer(in FileOrDirectoryArtifact fileOrDirectory)
        {
            Contract.Requires(fileOrDirectory.IsValid);
            return m_producerGetter?.Invoke(fileOrDirectory) ?? PipId.Invalid;
        }

        public ModuleId[] GetModuleId(string moduleName)
        {
            return new ModuleId[0];
        }

        public bool TryGetCachedOutputs(PipFilter pipFilter, out IReadOnlySet<FileOrDirectoryArtifact> outputs)
        {
            return m_cachedOutputs.TryGetValue(pipFilter, out outputs);
        }

        public void CacheOutputs(PipFilter pipFilter, IReadOnlySet<FileOrDirectoryArtifact> outputs)
        {
            m_cachedOutputs[pipFilter] = outputs;
        }

        private class CachedOutputKeyComparer : IEqualityComparer<PipFilter>
        {
            public bool Equals(PipFilter x, PipFilter y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(PipFilter obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
