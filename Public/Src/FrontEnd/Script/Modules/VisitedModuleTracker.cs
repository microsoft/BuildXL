// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Helper class that tracks all visitited (i.e. evaluated) modules if enabled.
    /// </summary>
    public sealed class VisitedModuleTracker
    {
        private enum TrackingKind
        {
            NoTracking,
            TrackImportedModules,
            TrackEverything,
        }

        private readonly TrackingKind m_trackingKind;

        [CanBeNull]
        private ConcurrentDictionary<ModuleLiteral, ImmutableContextBase> m_visitedModules;

        [NotNull]
        private ConcurrentDictionary<ModuleLiteral, ImmutableContextBase> VisitedModules
        {
            get { return LazyInitializer.EnsureInitialized(ref m_visitedModules, () => new ConcurrentDictionary<ModuleLiteral, ImmutableContextBase>()); }
        }

        /// <summary>
        /// Full tracking is enabled only when <paramref name="trackingKind"/> is <see cref="TrackingKind.TrackEverything"/>.
        /// If <paramref name="trackingKind"/> is <see cref="TrackingKind.TrackImportedModules"/> only imported modules will be tracked.
        /// Otherwise (like for configuration evaluation) no tracking will be used.
        /// </summary>
        /// <remarks>
        /// The tracking is enabled only when the debugger is presented.
        /// </remarks>
        private VisitedModuleTracker(TrackingKind trackingKind)
        {
            m_trackingKind = trackingKind;
        }

        /// <nodoc />
        public static VisitedModuleTracker Create(bool isDebug)
        {
            var trackingKind = isDebug ? TrackingKind.TrackEverything : TrackingKind.TrackImportedModules;
            return new VisitedModuleTracker(trackingKind);
        }

        /// <nodoc />
        public void Track(ModuleLiteral module, ImmutableContextBase context)
        {
            if (m_trackingKind == TrackingKind.TrackEverything)
            {
                VisitedModules.TryAdd(module, context);
            }
        }

        /// <nodoc />
        public IReadOnlyList<ModuleAndContext> GetVisitedModules()
        {
            return
                m_visitedModules?.Select(kvp => new ModuleAndContext(kvp.Key, kvp.Value.ContextTree)).ToArray()
                ?? CollectionUtilities.EmptyArray<ModuleAndContext>();
        }

        /// <nodoc />
        public static VisitedModuleTracker Disabled { get; } = new VisitedModuleTracker(TrackingKind.NoTracking);
    }
}
