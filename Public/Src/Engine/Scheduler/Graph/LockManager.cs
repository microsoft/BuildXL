// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Threading;
using Process = BuildXL.Pips.Operations.Process;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Provides fine-grained read/write (ie shared/exclusive) locking for paths and pips.
    /// </summary>
    /// <remarks>
    /// This structure manages four types of locks described below. All locks except the
    /// global lock are pooled to keep memory overhead to a minimum (ie there doesn't need to be
    /// a persistent pip or path lock for pips that are not currently being accessed).
    ///
    /// Global lock: This the widest lock which is used to perform arbitrary/cross-cutting
    /// state accesses and modifications. Taking the exclusive lock ensures that other locks
    /// cannot be acquired until the global lock is released (namely the other locks internally
    /// acquire a global shared lock). A shared lock may be taken explicitly as well. A exclusive lock
    /// should not be taken inside a shared lock.
    /// When to use: The lock requirements for a particular block are unknown/cross-cutting.
    ///
    /// Path group lock: Used to lock access to a series of paths accessed by a pip. Read locks
    /// are taken for input files and write locks are taken for output files. Locks are taken as an
    /// atomic operation by taking them in a consistent order to prevent deadlocks. This lock can create
    /// exclusive locks for input files as described below.
    /// When to use: Adding pips to graph. To ensure input/output validation and registration is synchronized.
    ///
    /// Exclusive path lock: This is a lock which can only be acquired inside a path group which has
    /// access to the path (read or write). It is used to synchronize operations finely for particular paths.
    /// Namely for use with <see cref="PipGraph.Builder.EnsurePipExistsForSourceArtifact"/> because a read lock will
    /// be taken for source files, but may require modification of state for the path. Only ONE exclusive
    /// inner lock can be outstanding for a path group to guard against deadlocks by acquiring these locks
    /// out of order.
    /// When to use: When fine-grained exclusive synchronization is desired for a input file/directory (ie
    /// a path for which a read lock is taken).
    ///
    /// Pip lock: Locks modification to state for a pip. This should only be taken after pip has been added
    /// to graph and has a valid pip id.
    /// When to use: when locking one (modify the pips state such as ref counts)
    /// or two pips (such as registering a dependency).
    /// </remarks>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    internal sealed class LockManager
    {
        /// <summary>
        /// Global lock used to allow arbitrary state access within a particular scope
        /// </summary>
        private readonly ReadWriteLock m_globalLock = ReadWriteLock.Create();

        /// <summary>
        /// Defines scoped references to path locks. See comment on class definition for details.
        /// </summary>
        private readonly ScopedLockMap<(AbsolutePath, LockType)> m_scopedPathLockMap;

        /// <summary>
        /// Defines scoped references to pip locks. See comment on class definition for details.
        /// </summary>
        private readonly ScopedLockMap<PipId> m_scopedPipLockMap;

        /// <summary>
        /// Pools the path lock builders
        /// </summary>
        private readonly ConcurrentBag<PathAccessGroupLock> m_pathLockPool;

#if DEBUG
        /// <summary>
        /// Tracks the number of outstanding non-global locks to ensure that global lock is not
        /// acquired inside path or pip lock.
        /// </summary>
        private readonly ThreadLocal<int> m_outstandingNonGlobalLocks = new ThreadLocal<int>(() => 0);
#endif

        /// <summary>
        /// Tracks the number of outstanding shared locks to ensure that exclusive locks cannot be
        /// acquired inside shared locks. Also, prevents re-entrancy into shared lock by making any
        /// inner acquisitions of a shared lock inside a shared lock a no-op.
        /// </summary>
        private readonly ThreadLocal<int> m_outstandingGlobalSharedLocks = new ThreadLocal<int>(() => 0);

        /// <summary>
        /// The access verification to ensure scopes can only be created by this object.
        /// </summary>
        private readonly object m_accessVerifier = new object();

        /// <summary>
        /// Class constructor.
        /// </summary>
        public LockManager()
        {
            m_scopedPathLockMap = new ScopedLockMap<(AbsolutePath, LockType)>();
            m_scopedPipLockMap = new ScopedLockMap<PipId>();
            m_pathLockPool = new ConcurrentBag<PathAccessGroupLock>();
        }

        ~LockManager()
        {
#if DEBUG
            m_outstandingNonGlobalLocks.Dispose();
#endif
            m_outstandingGlobalSharedLocks.Dispose();
        }

        #region Global Locking

        /// <summary>
        /// Indicates if the current thread has acquired the global exclusive lock
        /// </summary>
        public bool HasGlobalExclusiveAccess => m_globalLock.HasExclusiveAccess;

        /// <summary>
        /// Acquires a shared lock for global state if applicable. If exclusive global lock is already taken,
        /// this is not needed and an 'invalid' lock is returned that no-ops on Dispose().
        /// </summary>
        public GlobalSharedLock AcquireGlobalSharedLockIfApplicable()
        {
            if (!m_globalLock.HasExclusiveAccess)
            {
                return new GlobalSharedLock(this, m_accessVerifier);
            }
            else
            {
                return default(GlobalSharedLock);
            }
        }

        /// <summary>
        /// Acquires the exclusive global lock for arbitrary state access and manipulation. All other locks
        /// generated by this class will wait for this lock to be released unless owned by the calling thread.
        /// </summary>
        /// <remarks>
        /// WARNING: The global exclusive lock should not be acquired in the context of a PipLock or PathAccessLock
        /// as this may cause deadlocks.
        /// </remarks>
        public WriteLock AcquireGlobalExclusiveLock()
        {
#if DEBUG
            Debug.Assert(m_outstandingNonGlobalLocks.Value == 0, "Cannot acquire global lock inside pip lock or path group lock");
#endif
            Debug.Assert(m_outstandingGlobalSharedLocks.Value == 0, "Cannot acquire global exclusive lock inside of a shared lock, pip lock, or path group lock");

            return m_globalLock.AcquireWriteLock();
        }

        #endregion Global Locking

        #region Pip Locking

        /// <summary>
        /// Gets whether the current thread has exclusive access to the pip
        /// </summary>
        public bool HasExclusivePipAccess(PipId pipId)
        {
            if (HasGlobalExclusiveAccess)
            {
                return true;
            }

            using (var scope = m_scopedPipLockMap.OpenScope(pipId))
            {
                return scope.Value.HasExclusiveAccess;
            }
        }

        /// <summary>
        /// Acquires exclusive access to modify state for a particular pip
        /// </summary>
        public PipLock AcquireLock(PipId pipId)
        {
            return new PipLock(this, pipId, PipId.Invalid, m_accessVerifier);
        }

        /// <summary>
        /// Acquires exclusive access to modify state of a pair of pips. The pip with
        /// the minimum pip id is always taken for consistency
        /// </summary>
        public PipLock AcquireLocks(PipId pipId1, PipId pipId2)
        {
            Contract.Requires(pipId1.IsValid);
            Contract.Requires(pipId2.IsValid);
            Contract.Requires(pipId1 != pipId2);

            // Ensure minimum pip id is always taken first
            if (pipId1.Value > pipId2.Value)
            {
                var tmp = pipId1;
                pipId1 = pipId2;
                pipId2 = tmp;
            }

            return new PipLock(this, pipId1, pipId2, m_accessVerifier);
        }

        #endregion Pip Locking

        #region Path Locking

        /// <summary>
        /// Gets a path lock builder to use to synchronize accesses to paths.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private PathAccessGroupLock.Builder GetPathAccessLockBuilder()
        {
            PathAccessGroupLock pathAccessLock;
            if (!m_pathLockPool.TryTake(out pathAccessLock))
            {
                pathAccessLock = new PathAccessGroupLock(this, m_accessVerifier);
            }

            return new PathAccessGroupLock.Builder(this, pathAccessLock, m_accessVerifier);
        }

        /// <summary>
        /// Gets a path access lock for the files accessed by the pip
        /// </summary>
        public PathAccessGroupLock AcquirePathAccessLock(Process process)
        {
            var builder = GetPathAccessLockBuilder();
            builder.AddAccesses(process.FileOutputs, AccessType.Write);
            builder.AddAccesses(process.DirectoryOutputs, AccessType.Write);
            builder.AddAccesses(process.Dependencies, AccessType.Read);
            builder.AddAccesses(process.DirectoryDependencies, AccessType.Read);
            return builder.Acquire();
        }

        /// <summary>
        /// Gets a path access lock for the files accessed by the pip
        /// </summary>
        public PathAccessGroupLock AcquirePathAccessLock(IpcPip ipcPip)
        {
            var builder = GetPathAccessLockBuilder();
            builder.AddAccesses(ipcPip.FileDependencies, AccessType.Read);
            builder.AddAccesses(ipcPip.DirectoryDependencies, AccessType.Read);
            builder.AddAccess(ipcPip.OutputFile, AccessType.Write);
            return builder.Acquire();
        }

        /// <summary>
        /// Gets a path access lock for the files accessed by the pip
        /// </summary>
        public PathAccessGroupLock AcquirePathAccessLock(WriteFile writeFile)
        {
            var builder = GetPathAccessLockBuilder();
            builder.AddAccess(writeFile.Destination, AccessType.Write);
            return builder.Acquire();
        }

        /// <summary>
        /// Gets a path access lock for the files accessed by the pip
        /// </summary>
        public PathAccessGroupLock AcquirePathAccessLock(CopyFile copyFile)
        {
            var builder = GetPathAccessLockBuilder();
            builder.AddAccess(copyFile.Destination, AccessType.Write);
            builder.AddAccess(copyFile.Source, AccessType.Read);
            return builder.Acquire();
        }

        /// <summary>
        /// Gets a path access lock for the files accessed by the pip
        /// </summary>
        public PathAccessGroupLock AcquirePathAccessLock(SealDirectory sealDirectory)
        {
            var builder = GetPathAccessLockBuilder();
            builder.AddAccess(sealDirectory.DirectoryRoot, AccessType.Read);
            builder.AddAccesses(sealDirectory.Contents, AccessType.Read);
            return builder.Acquire();
        }

        #endregion Path Locking

        /// <summary>
        /// Defines a type of path lock
        /// </summary>
        private enum LockType
        {
            /// <summary>
            /// Denotes locks using in a path group lock (ie a lock on a group of paths as a
            /// single operation).
            /// </summary>
            PathGroup,

            /// <summary>
            /// Denotes locks using a single path lock. In path groups, source files
            /// take a read lock, but the scheduler may need to create a pip for the source
            /// file in a thread-safe fashion so these locks are used to take a fine-grained write
            /// lock inside of the path group lock.
            /// </summary>
            SinglePath,
        }

        /// <summary>
        /// Defines a access type required by the path
        /// </summary>
        public enum AccessType
        {
            /// <summary>
            /// Read access to the path's info is used
            /// </summary>
            Read,

            /// <summary>
            /// Write access to the path's info is used
            /// </summary>
            Write,
        }

        /// <summary>
        /// Separate helper class to have shared pool of locks
        /// </summary>
        private static class ScopedLockMap
        {
            // TODO: Implement better ConcurrentBag that doesn't allocate on every add.
            public static readonly ConcurrentQueue<ReadWriteLock> Locks = new ConcurrentQueue<ReadWriteLock>();
        }

        /// <summary>
        /// Implements a scoped reference map using pooled reader writer locks. For a particular key,
        /// a lock is reserved from the pool while there are scopes open and returned to the pool when
        /// all scopes for that key are closed.
        /// </summary>
        private sealed class ScopedLockMap<TKey> : ScopedReferenceMap<TKey, ReadWriteLock>
        {
            public ScopedLockMap()
            {
            }

            protected override ReadWriteLock CreateValue(TKey key)
            {
                ReadWriteLock rwLock;
                if (!ScopedLockMap.Locks.TryDequeue(out rwLock))
                {
                    rwLock = ReadWriteLock.Create();
                }

                return rwLock;
            }

            protected override void ReleaseValue(TKey key, ReadWriteLock value)
            {
                ScopedLockMap.Locks.Enqueue(value);
            }
        }

        /// <summary>
        /// Represents an exclusive lock on a path
        /// </summary>
        public readonly struct ExclusiveSinglePathLock : IDisposable
        {
            /// <summary>
            /// The lock scope for the pip
            /// </summary>
            private readonly ScopedReferenceMap<(AbsolutePath absolutePath, LockType lockType), ReadWriteLock>.Scope m_pathScope;

            /// <summary>
            /// The parent group lock
            /// </summary>
            private readonly PathAccessGroupLock m_parentLock;

            /// <summary>
            /// The owning lock manager
            /// </summary>
            private readonly LockManager m_owner;

            /// <summary>
            /// Creates a exclusive lock on the path. Internal use only.
            /// </summary>
            internal ExclusiveSinglePathLock(LockManager owner, PathAccessGroupLock groupLock, AbsolutePath path, object accessVerifier)
            {
                Contract.Requires(path.IsValid);
                Contract.Assert(owner.m_accessVerifier == accessVerifier, "ExclusiveSinglePathLock can only be created by a parent PathAccessGroupLock");

                m_owner = owner;
                m_parentLock = groupLock;

                // Acquire the exclusive lock for the path
                m_pathScope = owner.m_scopedPathLockMap.OpenScope((path, LockType.SinglePath));
                m_pathScope.Value.EnterWriteLock();
            }

            /// <summary>
            /// Releases the path lock
            /// </summary>
            public void Dispose()
            {
                if (m_pathScope.Key.absolutePath.IsValid)
                {
                    m_pathScope.Value.ExitWriteLock();

                    m_pathScope.Dispose();

                    m_parentLock.OnPathInnerExclusiveLockReleased(m_owner.m_accessVerifier);
                }
            }
        }

        /// <summary>
        /// Represents an exclusive lock
        /// </summary>
        public readonly struct GlobalSharedLock : IDisposable
        {
            /// <summary>
            /// The lock scope for the pip
            /// </summary>
            private readonly LockManager m_owner;
            private readonly ReadLock m_readLock;

            internal GlobalSharedLock(LockManager owner, object accessVerifier)
            {
                Contract.Requires(owner != null);
                Contract.Assert(owner.m_accessVerifier == accessVerifier, "PipLocks can only be created by a parent LockManager");

                m_owner = owner;

                // Need to ensure that global shared lock is only acquired once per thread. Otherwise,
                // deadlock may occur if a shared lock is acquired on thread A, then an attempt to acquire exclusive lock
                // happens on another thread B. When acquiring second read lock on A, it will wait for B which is
                // already waiting on A, thus a deadlock.
                var outstandingGlobalSharedLocks = m_owner.m_outstandingGlobalSharedLocks.Value;
                if (outstandingGlobalSharedLocks == 0)
                {
                    m_readLock = m_owner.m_globalLock.AcquireReadLock();
                    m_owner.m_outstandingGlobalSharedLocks.Value = 1;
                }
                else
                {
                    m_readLock = ReadLock.Invalid;
                }
            }

            /// <summary>
            /// Releases the pip lock
            /// </summary>
            [SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", Justification = "Used in Debug.Assert")]
            public void Dispose()
            {
                if (m_readLock.IsValid)
                {
                    var outstandingGlobalSharedLocks = m_owner.m_outstandingGlobalSharedLocks.Value;
                    Debug.Assert(outstandingGlobalSharedLocks == 1, "Expect a single outstanding global shared lock");
                    m_owner.m_outstandingGlobalSharedLocks.Value = 0;

                    m_readLock.Dispose();
                }
            }
        }

        /// <summary>
        /// Represents an exclusive lock on a pip
        /// </summary>
        public readonly struct PipLock : IDisposable
        {
            /// <summary>
            /// The lock scope for the pip
            /// </summary>
            private readonly LockManager m_owner;
            private readonly ScopedReferenceMap<PipId, ReadWriteLock>.Scope m_minScope;
            private readonly ScopedReferenceMap<PipId, ReadWriteLock>.Scope m_maxScope;
            private readonly GlobalSharedLock m_globalSharedLock;

            internal PipLock(LockManager owner, PipId pipId1, PipId pipId2, object accessVerifier)
            {
                Contract.Requires(owner != null);
                Contract.Requires(pipId1.IsValid);
                Contract.Assert(owner.m_accessVerifier == accessVerifier, "PipLocks can only be created by a parent LockManager");

                m_owner = owner;
#if DEBUG
                m_owner.m_outstandingNonGlobalLocks.Value = m_owner.m_outstandingNonGlobalLocks.Value + 1;
#endif

                // Try to acquire the global shared lock
                m_globalSharedLock = owner.AcquireGlobalSharedLockIfApplicable();

                m_minScope = owner.m_scopedPipLockMap.OpenScope(pipId1);
                m_minScope.Value.EnterWriteLock();

                // Second pip id may be invalid if this is only being used to lock a single pip
                if (pipId2.IsValid)
                {
                    m_maxScope = owner.m_scopedPipLockMap.OpenScope(pipId2);
                    m_maxScope.Value.EnterWriteLock();
                }
                else
                {
                    m_maxScope = default(ScopedReferenceMap<PipId, ReadWriteLock>.Scope);
                }
            }

            /// <summary>
            /// Releases the pip lock
            /// </summary>
            public void Dispose()
            {
                m_globalSharedLock.Dispose();

                if (m_maxScope.Key.IsValid)
                {
                    m_maxScope.Value.ExitWriteLock();
                }

                m_maxScope.Dispose();

                if (m_minScope.Key.IsValid)
                {
                    m_minScope.Value.ExitWriteLock();
                }

#if DEBUG
                if (m_owner != null)
                {
                    m_owner.m_outstandingNonGlobalLocks.Value = m_owner.m_outstandingNonGlobalLocks.Value - 1;
                }
#endif

                m_minScope.Dispose();
            }
        }

        /// <summary>
        /// Defines a series of read-write locks on paths depending on the access required.
        /// </summary>
        public sealed class PathAccessGroupLock : IDisposable
        {
            /// <summary>
            /// Comparer used to sort path accesses by path. The access type does not need to be sorted since there can only be one access
            /// type for a particular path (namely writes taken precedence).
            /// </summary>
            private static readonly Comparer<PathAccess> s_pathAndAccessComparer = Comparer<PathAccess>.Create((p1, p2) => Comparer<int>.Default.Compare(p1.Path.Value.Value, p2.Path.Value.Value));

            /// <summary>
            /// Tracks whether inputs have been added to the group
            /// </summary>
            private bool m_hasAddedInputs;

            /// <summary>
            /// The owning lock manager
            /// </summary>
            private readonly LockManager m_owner;

            /// <summary>
            /// The set of paths accessed. This is used to deduplicate path accesses and verify
            /// that path exists in path group for creating an exclusive path lock
            /// </summary>
            private readonly Dictionary<AbsolutePath, AccessType> m_pathAccessMap = new Dictionary<AbsolutePath, AccessType>();

            /// <summary>
            /// The list of path accesses.
            /// </summary>
            private readonly List<PathAccess> m_pathAccesses = new List<PathAccess>();

            /// <summary>
            /// The ordered set of locks matching the path accesses list
            /// </summary>
            private readonly List<ScopedReferenceMap<(AbsolutePath, LockType), ReadWriteLock>.Scope> m_orderedLocks =
                new List<ScopedReferenceMap<(AbsolutePath, LockType), ReadWriteLock>.Scope>();

            /// <summary>
            /// Global shared lock if this lock was not acquired in the context of a global exclusive lock. This ensures
            /// synchronization with code which acquires the global exclusive lock
            /// </summary>
            private GlobalSharedLock m_globalSharedLock;

            /// <summary>
            /// Used to verify only a single exclusive path lock is taken at a time
            /// </summary>
            private bool m_hasOutstandingExclusiveLock;

            /// <summary>
            /// Frees the paths accessed by the lock and returns the lock.
            /// </summary>
            /// <remarks>
            /// WARNING: This object should not be used after disposal.
            /// </remarks>
            public void Dispose()
            {
                Contract.Assert(!m_hasOutstandingExclusiveLock, "Dispose all inner locks before disposing parent path group lock");

                for (int i = m_pathAccesses.Count - 1; i >= 0; i--)
                {
                    var pathAccess = m_pathAccesses[i];
                    var lockScope = m_orderedLocks[i];

                    if (pathAccess.AccessType == AccessType.Read)
                    {
                        lockScope.Value.ExitReadLock();
                    }
                    else
                    {
                        lockScope.Value.ExitWriteLock();
                    }

                    // Release the scope so the path lock can be returned to the pool if
                    // no further references
                    lockScope.Dispose();
                }

                // Release the global shared lock if acquired.
                m_globalSharedLock.Dispose();

                m_hasAddedInputs = false;
                m_globalSharedLock = default(GlobalSharedLock);
                m_pathAccesses.Clear();
                m_pathAccessMap.Clear();
                m_orderedLocks.Clear();

                // Add this group lock back to the pool
                m_owner.m_pathLockPool.Add(this);

#if DEBUG
                m_owner.m_outstandingNonGlobalLocks.Value = m_owner.m_outstandingNonGlobalLocks.Value - 1;
#endif
            }

            /// <summary>
            /// Constructs the path group lock. Internal use only.
            /// </summary>
            internal PathAccessGroupLock(LockManager owner, object accessVerifier)
            {
                Contract.Assert(owner.m_accessVerifier == accessVerifier, "PathAccessLocks can only be created by a parent LockManager");
                m_owner = owner;
            }

            /// <summary>
            /// Indicates if the lock has read access to the specified path
            /// </summary>
            /// <param name="path">the accessed path</param>
            /// <returns>true if read access is available for the path</returns>
            [Pure]
            public bool HasReadAccess(AbsolutePath path)
            {
                // NOTE: We don't check the access type since write access also includes read access
                return m_pathAccessMap.ContainsKey(path);
            }

            /// <summary>
            /// Indicates if the lock has write access to the specified path
            /// </summary>
            /// <param name="path">the accessed path</param>
            /// <returns>true if write access is available for the path</returns>
            [Pure]
            public bool HasWriteAccess(AbsolutePath path)
            {
                AccessType accessType;
                if (m_pathAccessMap.TryGetValue(path, out accessType))
                {
                    return accessType == AccessType.Write;
                }

                return false;
            }

            /// <summary>
            /// Creates an exclusive lock for a path contained in this group lock.
            /// Group lock must have access to path and no other exclusive locks can be outstanding
            /// (ie only a single inner exclusive is allowed at a time).
            /// </summary>
            public ExclusiveSinglePathLock AcquirePathInnerExclusiveLock(AbsolutePath path)
            {
                Contract.Assert(m_pathAccessMap.ContainsKey(path), "Cannot acquire exclusive lock for path not in group");
                Contract.Assert(!m_hasOutstandingExclusiveLock, "Cannot acquire more that on exclusive inner lock for a path group");
                m_hasOutstandingExclusiveLock = true;
                return new ExclusiveSinglePathLock(m_owner, this, path, m_owner.m_accessVerifier);
            }

            /// <summary>
            /// Releases the inner lock allowing acquire another inner lock
            /// </summary>
            public void OnPathInnerExclusiveLockReleased(object accessVerifier)
            {
                Contract.Assert(m_owner.m_accessVerifier == accessVerifier, "Inner lock release notification should only occur as a result of releasing the lock via Dispose");
                Contract.Assert(m_hasOutstandingExclusiveLock, "Disposed called twice on inner exclusive lock");
                m_hasOutstandingExclusiveLock = false;
            }

            [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
            public readonly struct Builder
            {
                private readonly PathAccessGroupLock m_pathLock;

                /// <summary>
                /// Constructor. Internal access only.
                /// </summary>
                internal Builder(LockManager owner, PathAccessGroupLock pathLock, object accessVerifier)
                {
                    Contract.Assert(owner.m_accessVerifier == accessVerifier, "PathAccessLocks can only be created by a parent LockManager");
                    m_pathLock = pathLock;
                }

                /// <summary>
                /// Registers accesses to paths
                /// </summary>
                /// <remarks>
                /// Outputs must be added prior to inputs.
                /// </remarks>
                /// <param name="paths">the files accessed</param>
                /// <param name="accessType">indicates is files is written or read</param>
                public void AddAccesses(ReadOnlyArray<FileArtifact> paths, AccessType accessType)
                {
                    m_pathLock.m_hasAddedInputs |= accessType == AccessType.Read;
                    Contract.Assert(accessType == AccessType.Read || !m_pathLock.m_hasAddedInputs, "Outputs must be added before inputs");
                    foreach (var path in paths)
                    {
                        AddAccess(path.Path, accessType);
                    }
                }

                /// <summary>
                /// Registers accesses to paths
                /// </summary>
                /// <remarks>
                /// Outputs must be added prior to inputs.
                /// </remarks>
                /// <param name="paths">the files accessed</param>
                /// <param name="accessType">indicates is files is written or read</param>
                public void AddAccesses(ReadOnlyArray<FileArtifactWithAttributes> paths, AccessType accessType)
                {
                    m_pathLock.m_hasAddedInputs |= accessType == AccessType.Read;
                    Contract.Assert(accessType == AccessType.Read || !m_pathLock.m_hasAddedInputs, "Outputs must be added before inputs");
                    foreach (var path in paths)
                    {
                        AddAccess(path.Path, accessType);
                    }
                }

                /// <summary>
                /// Registers accesses to paths
                /// </summary>
                /// <remarks>
                /// Outputs must be added prior to inputs.
                /// </remarks>
                /// <param name="paths">the directories accessed</param>
                /// <param name="accessType">indicates is directories is written or read</param>
                public void AddAccesses(ReadOnlyArray<DirectoryArtifact> paths, AccessType accessType)
                {
                    m_pathLock.m_hasAddedInputs |= accessType == AccessType.Read;
                    Contract.Assert(accessType == AccessType.Read || !m_pathLock.m_hasAddedInputs, "Outputs must be added before inputs");
                    foreach (var path in paths)
                    {
                        AddAccess(path.Path, accessType);
                    }
                }

                /// <summary>
                /// Registers accesses to paths
                /// </summary>
                /// <remarks>
                /// Outputs must be added prior to inputs.
                /// </remarks>
                /// <param name="path">the path accessed</param>
                /// <param name="accessType">indicates is path is written or read</param>
                public void AddAccess(AbsolutePath path, AccessType accessType)
                {
                    if (!m_pathLock.m_pathAccessMap.ContainsKey(path))
                    {
                        m_pathLock.m_pathAccessMap.Add(path, accessType);
                        m_pathLock.m_pathAccesses.Add(new PathAccess(path, accessType));
                    }
                }

                /// <summary>
                /// Acquires locks for accessed paths in consistent order and returns the lock which can
                /// be disposed to free accesses to paths
                /// </summary>
                public PathAccessGroupLock Acquire()
                {
                    // Try to acquire the global shared lock
                    m_pathLock.m_globalSharedLock = m_pathLock.m_owner.AcquireGlobalSharedLockIfApplicable();

                    // Sort the paths so we get a consistent acquisition order
                    m_pathLock.m_pathAccesses.Sort(s_pathAndAccessComparer);

                    foreach (var pathAccess in m_pathLock.m_pathAccesses)
                    {
                        ScopedReferenceMap<(AbsolutePath, LockType), ReadWriteLock>.Scope scopedLock = m_pathLock.m_owner.m_scopedPathLockMap.OpenScope((pathAccess.Path, LockType.PathGroup));
                        if (pathAccess.AccessType == AccessType.Read)
                        {
                            scopedLock.Value.EnterReadLock();
                        }
                        else
                        {
                            scopedLock.Value.EnterWriteLock();
                        }

                        m_pathLock.m_orderedLocks.Add(scopedLock);
                    }

#if DEBUG
                    m_pathLock.m_owner.m_outstandingNonGlobalLocks.Value = m_pathLock.m_owner.m_outstandingNonGlobalLocks.Value + 1;
#endif
                    return m_pathLock;
                }
            }
        }

        /// <summary>
        /// Defines a path access
        /// </summary>
        private readonly struct PathAccess
        {
            /// <summary>
            /// The type of access required for the path
            /// </summary>
            public readonly AccessType AccessType;

            /// <summary>
            /// The path accessed
            /// </summary>
            public readonly AbsolutePath Path;

            public PathAccess(AbsolutePath path, AccessType accessType)
            {
                Path = path;
                AccessType = accessType;
            }
        }
    }
}
