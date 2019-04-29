// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities;
using VSCode.DebugAdapter;
using VSCode.DebugProtocol;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// A tuple class comprised of Breakpoint and its affiliated node.
    /// </summary>
    internal sealed class BreakpointRecord
    {
        /// <summary>
        ///     The breakpoint.
        /// </summary>
        internal IBreakpoint Breakpoint { get; }

        /// <summary>
        ///     The node affiliated with this breakpoint. Will be null the first time we hit it.
        /// </summary>
        internal Node Node
        {
            get; set;
        }

        internal BreakpointRecord(ISourceBreakpoint sbp)
        {
            Breakpoint = new Breakpoint(true, sbp.Line);
        }
    }

    /// <summary>
    /// Static factory for creating breakpoint stores.
    /// </summary>
    public static class BreakpointStoreFactory
    {
        /// <summary>Creates and returns a new instance of a <see cref="IMasterStore"/></summary>
        public static IMasterStore CreateMaster() => new MasterStoreWithLocking();

        /// <summary>Creates and returns a new instance of a <see cref="IBreakpointStore"/></summary>
        public static IBreakpointStore CreateProxy(IMasterStore master) => new ReadOnlyProxyStore(master);

        // ===========================================================================================================
        // == Shared pure static methods for internal (within this file) use.
        // ===========================================================================================================
        internal static IBreakpoint SearchRecords(IReadOnlyList<BreakpointRecord> breakpointsForSource, Node node)
        {
            // 1) Check if the specified line is set with a BP.
            BreakpointRecord record = breakpointsForSource.FirstOrDefault(bp => bp.Breakpoint.Line == node.Location.Line);
            if (record == null)
            {
                return null;
            }

            // 2) Check if a node is affiliated with this BP.
            if (record.Node != null)
            {
                if (record.Node == node)
                {
                    // We hit both same line and same node.
                    // (This happens during a recursive call or in a loop)
                    return record.Breakpoint;
                }
                else
                {
                    // We hit the same line, but not same node.
                    return null;
                }
            }
            else
            {
                // This is the first time we hit this line. Associate the node with this BP.
                record.Node = node;
                return record.Breakpoint;
            }
        }

        internal static IReadOnlyList<BreakpointRecord> UpdateBreakpointRecords(IReadOnlyList<ISourceBreakpoint> newBreakpoints, IReadOnlyList<BreakpointRecord> currentBreakpoints)
        {
            Contract.Requires(newBreakpoints != null);
            Contract.Ensures(Contract.Result<IReadOnlyList<BreakpointRecord>>() != null);
            Contract.Ensures(Contract.Result<IReadOnlyList<BreakpointRecord>>().Count == newBreakpoints.Count);

            // convert to BreakpointRecord
            var breakpoints = newBreakpoints.Select(b => new BreakpointRecord(b)).ToArray();

            // if 0 breakpoints given --> return early
            if (breakpoints.Length == 0)
            {
                return breakpoints;
            }

            if (currentBreakpoints != null)
            {
                // If we already have any breakpoint in this file, try to preserve their
                // node affinity if they also appear in the new breakpoint list.

                // Get nodes currently associated with each breakpoint
                var currentBreakpointSet = new Dictionary<int, Node>();
                foreach (var bpr in currentBreakpoints)
                {
                    if (bpr.Node != null)
                    {
                        currentBreakpointSet[bpr.Breakpoint.Line] = bpr.Node;
                    }
                }

                // Store them in the new breakpoint if line number matches
                foreach (var bpr in breakpoints)
                {
                    Node node;
                    if (currentBreakpointSet.TryGetValue(bpr.Breakpoint.Line, out node))
                    {
                        bpr.Node = node;
                    }
                }
            }

            return breakpoints;
        }

        // ===========================================================================================================
        // == Private Implementations of interfaces declared above (IBreakpointStore, IMasterStore).
        // ===========================================================================================================

        /// <summary>
        ///     Super slow.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1812:InternalClassNeverInstantiated")]
        private sealed class MasterStoreWithConcurrentDictionary : IMasterStore
        {
            private int m_version;

            /// <summary>
            ///     Maps a source path to a list of <code cref="IBreakpoint"/>s.
            /// </summary>
            private readonly ConcurrentDictionary<AbsolutePath, IReadOnlyList<BreakpointRecord>> m_breakpoints;

            /// <inheritdoc/>
            public int Version => Volatile.Read(ref m_version);

            /// <summary>Constructor. Creates a new store with <see cref="Version"/> set to 1.</summary>
            internal MasterStoreWithConcurrentDictionary()
            {
                m_version = 0;
                m_breakpoints = new ConcurrentDictionary<AbsolutePath, IReadOnlyList<BreakpointRecord>>();
            }

            /// <inheritdoc/>
            public bool IsEmpty => m_breakpoints.IsEmpty;

            /// <inheritdoc/>
            public IReadOnlyList<IBreakpoint> Set(AbsolutePath source, IReadOnlyList<ISourceBreakpoint> sourceBreakpoints)
            {
                IncrementVersion();

                // remove existing breakpoints
                IReadOnlyList<BreakpointRecord> currentRecords;
                var breakpointsForSourceExisted = m_breakpoints.TryRemove(source, out currentRecords);
                var records = UpdateBreakpointRecords(sourceBreakpoints, breakpointsForSourceExisted ? currentRecords : null);

                // if 0 breakpoints given --> already removed from dictionary, so just return
                if (records.Count == 0)
                {
                    return new IBreakpoint[0];
                }
                else
                {
                    m_breakpoints[source] = records;
                    return records.Select(b => b.Breakpoint).ToArray();
                }
            }

            /// <inheritdoc/>
            public void Clear()
            {
                IncrementVersion();
                m_breakpoints.Clear();
            }

            /// <inheritdoc/>
            public IBreakpoint Find(AbsolutePath source, Node node)
            {
                IReadOnlyList<BreakpointRecord> breakpointsForSource;
                return m_breakpoints.TryGetValue(source, out breakpointsForSource)
                    ? SearchRecords(breakpointsForSource, node)
                    : null;
            }

            /// <summary>
            ///     Returns a clone of this store which is not thread-safe and implements not locking.
            /// </summary>
            public IBreakpointStore Clone()
            {
                var dict = m_breakpoints.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                return new DictBackedReadOnlyStore(Version, dict);
            }

            private void IncrementVersion()
            {
                Interlocked.Increment(ref m_version);
            }

            object ICloneable.Clone() => Clone();
        }

        /// <summary>
        ///     Thread-safe implementation of <see cref="IMasterStore"/> that uses locking.
        /// </summary>
        private sealed class MasterStoreWithLocking : IMasterStore
        {
            private int m_version;

            /// <summary>
            /// Maps a source path to a list of <code cref="IBreakpoint"/>s.
            /// </summary>
            // (TODO: consider using AbsolutePath as key)
            private readonly IDictionary<AbsolutePath, IReadOnlyList<BreakpointRecord>> m_breakpoints;

            /// <inheritdoc/>
            public int Version => m_version;

            /// <summary>Constructor.  Creates a new store with <see cref="Version"/> set to 1.</summary>
            public MasterStoreWithLocking()
            {
                m_version = 0;
                m_breakpoints = new Dictionary<AbsolutePath, IReadOnlyList<BreakpointRecord>>();
            }

            /// <inheritdoc/>
            public bool IsEmpty => m_breakpoints.Count == 0;

            /// <inheritdoc/>
            public IReadOnlyList<IBreakpoint> Set(AbsolutePath source, IReadOnlyList<ISourceBreakpoint> sourceBreakpoints)
            {
                lock (this)
                {
                    IncrementVersion();

                    var currentBreakpoints = m_breakpoints.ContainsKey(source)
                        ? m_breakpoints[source]
                        : null;
                    var records = UpdateBreakpointRecords(sourceBreakpoints, currentBreakpoints);

                    // if 0 breakpoints given --> already removed from dictionary, so just return
                    if (records.Count == 0)
                    {
                        m_breakpoints.Remove(source);
                        return new IBreakpoint[0];
                    }
                    else
                    {
                        m_breakpoints[source] = records;
                        return records.Select(b => b.Breakpoint).ToArray();
                    }
                }
            }

            /// <inheritdoc/>
            public void Clear()
            {
                lock (this)
                {
                    IncrementVersion();
                    m_breakpoints.Clear();
                }
            }

            /// <inheritdoc/>
            public IBreakpoint Find(AbsolutePath source, Node node)
            {
                lock (this)
                {
                    IReadOnlyList<BreakpointRecord> breakpointsForSource;
                    return m_breakpoints.TryGetValue(source, out breakpointsForSource)
                        ? SearchRecords(breakpointsForSource, node)
                        : null;
                }
            }

            /// <summary>
            ///     Returns a clone of this store which is not thread-safe and implements not locking.
            /// </summary>
            public IBreakpointStore Clone()
            {
                lock (this)
                {
                    var dict = m_breakpoints.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    return new DictBackedReadOnlyStore(Version, dict);
                }
            }

            object ICloneable.Clone() => Clone();

            private void IncrementVersion()
            {
                Interlocked.Increment(ref m_version);
            }
        }

        /// <summary>
        ///     A simple dictionary-backed, non-thread-safe implementation of <see cref="IBreakpointStore"/>.
        /// </summary>
        private sealed class DictBackedReadOnlyStore : IBreakpointStore
        {
            private readonly int m_version;
            private readonly IDictionary<AbsolutePath, IReadOnlyList<BreakpointRecord>> m_dict;

            /// <inheritdoc/>
            public int Version => m_version;

            /// <inheritdoc/>
            public bool IsEmpty => m_dict.Count == 0;

            /// <inheritdoc/>
            public IBreakpoint Find(AbsolutePath source, Node node)
            {
                IReadOnlyList<BreakpointRecord> breakpointsForSource;
                return m_dict.TryGetValue(source, out breakpointsForSource)
                    ? SearchRecords(breakpointsForSource, node)
                    : null;
            }

            internal DictBackedReadOnlyStore(int version, IDictionary<AbsolutePath, IReadOnlyList<BreakpointRecord>> dict)
            {
                m_version = version;
                m_dict = dict;
            }
        }

        /// <summary>
        ///     An implementation of <see cref="IBreakpointStore"/> that has two different backing breakpoint
        ///     stores: one thread-safe master, and one simple proxy store.  All reads are performed from the
        ///     proxy store, as long as its version is the same as that of the master store; when the versions
        ///     are not the same, the proxy store is overwritten with a clone of the master.
        /// </summary>
        /// <remarks>
        ///     The motivation behind this implementation is to be able to do locking-free reads.
        /// </remarks>
        private sealed class ReadOnlyProxyStore : IBreakpointStore
        {
            private readonly IMasterStore m_master;
            private IBreakpointStore m_proxy;

            internal ReadOnlyProxyStore(IMasterStore master)
            {
                m_master = master;
                m_proxy = master.Clone();
            }

            /// <inheritdoc/>
            public int Version => m_proxy.Version;

            /// <inheritdoc/>
            public bool IsEmpty
            {
                get
                {
                    UpdateProxy();
                    return m_proxy.IsEmpty;
                }
            }

            /// <inheritdoc/>
            public IBreakpoint Find(AbsolutePath source, Node node)
            {
                UpdateProxy();
                return m_proxy.Find(source, node);
            }

            private void UpdateProxy()
            {
                if (m_proxy.Version != m_master.Version)
                {
                    m_proxy = m_master.Clone();
                }
            }
        }
    }
}
