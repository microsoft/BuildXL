// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Allows storing seal directories and creating matching directory artifacts.
    /// Also allows retrieving partial and full seals for file artifacts.
    /// </summary>
    /// <remarks>
    /// This class is thread-safe.
    /// </remarks>
    public sealed class SealedDirectoryTable
    {
        // Maps directory artifacts to Node with PipId and next node pointer
        private readonly ConcurrentBigMap<DirectoryArtifact, Node> m_seals;

        // Map directory path to info (the index of the first directory artifact in linked list of nodes
        // and the index of the fully sealed directory if any).
        private readonly ConcurrentBigMap<HierarchicalNameId, SealInfo> m_pathToSealInfo;

        private readonly PathTable m_pathTable;

        private long m_priorSealIndex;

        /// <summary>
        /// True if the SealDirectoryTable is ReadOnly
        /// </summary>
        public bool IsReadOnly { get; private set; }

        private bool? m_patchingState;

        public bool PatchingNeverStarted => m_patchingState == null;

        public bool IsPatching => m_patchingState == true;

        public bool DonePatching => m_patchingState == false;

        /// <summary>
        /// Class constructor
        /// </summary>
        public SealedDirectoryTable(PathTable pathTable)
        {
            IsReadOnly = false;
            m_patchingState = null;
            m_pathTable = pathTable;

            m_seals = new ConcurrentBigMap<DirectoryArtifact, Node>();
            m_pathToSealInfo = new ConcurrentBigMap<HierarchicalNameId, SealInfo>();
        }

        public ConcurrentBigMap<DirectoryArtifact, NodeId> FinishAndMarkReadOnly()
        {
            IsReadOnly = true;
            return m_seals.ConvertUnsafe(node => node.PipId.IsValid ? node.PipId.ToNodeId() : NodeId.Invalid);
        }

        /// <summary>
        /// Finds the <see cref="BuildXL.Utilities.DirectoryArtifact"/> for one of the <see cref="SealDirectory"/> pips that
        /// references the given file. Returns <see cref="DirectoryArtifact.Invalid"/> if no existing seal
        /// contains the file.
        /// </summary>
        /// <remarks>
        /// This implementation returns the smallest matching seal. This will hopefully
        /// be the most specific (handy for diagnostics).
        /// </remarks>
        public DirectoryArtifact TryFindSealDirectoryContainingFileArtifact(PipTable pipTable, FileArtifact artifact)
        {
            Contract.Requires(pipTable != null);

            return TryFindSealDirectoryEntryContainingFileArtifact(pipTable, artifact).Key;
        }

        /// <summary>
        /// Finds the <see cref="PipId"/> for one of the <see cref="SealDirectory"/> pips that
        /// references the given file. Returns <see cref="DirectoryArtifact.Invalid"/> if no existing seal
        /// contains the file.
        /// </summary>
        /// <remarks>
        /// This implementation returns the smallest matching seal. This will hopefully
        /// be the most specific (handy for diagnostics).
        /// </remarks>
        public PipId TryFindSealDirectoryPipContainingFileArtifact(PipTable pipTable, FileArtifact artifact)
        {
            Contract.Requires(pipTable != null);

            return TryFindSealDirectoryEntryContainingFileArtifact(pipTable, artifact).Value.PipId;
        }

        private KeyValuePair<DirectoryArtifact, Node> TryFindSealDirectoryEntryContainingFileArtifact(PipTable pipTable, FileArtifact artifact)
        {
            KeyValuePair<DirectoryArtifact, Node> smallestMatchingSeal = default(KeyValuePair<DirectoryArtifact, Node>);
            int smallestMatchingSealSize = int.MaxValue;
            int next = GetHeadNodeIndex(artifact);
            while (next >= 0)
            {
                var directoryAndSeal = m_seals.BackingSet[next];
                var sealDirectory =
                    (SealDirectory)
                        pipTable.HydratePip(
                            directoryAndSeal.Value.PipId,
                            PipQueryContext.SealedDirectoryTableTryFindDirectoryArtifactContainingFileArtifact);
                if (sealDirectory.Contents.Contains(artifact))
                {
                    if (!smallestMatchingSeal.Key.IsValid || smallestMatchingSealSize > sealDirectory.Contents.Length)
                    {
                        smallestMatchingSealSize = sealDirectory.Contents.Length;
                        smallestMatchingSeal = directoryAndSeal;
                    }
                }

                next = directoryAndSeal.Value.Next;
            }

            return smallestMatchingSeal;
        }

        private int GetHeadNodeIndex(FileArtifact artifact)
        {
            foreach (var current in m_pathTable.EnumerateHierarchyBottomUp(artifact.Path.Value, HierarchicalNameTable.NameFlags.Marked))
            {
                SealInfo sealInfo;
                if (m_pathToSealInfo.TryGetValue(current, out sealInfo))
                {
                    return sealInfo.HeadIndex;
                }
            }

            return -1;
        }

        /// <summary>
        ///     Get sealed directories and their corresponding pips.
        /// </summary>
        public IEnumerable<KeyValuePair<DirectoryArtifact, PipId>> GetSealedDirectories(AbsolutePath path)
        {
            SealInfo sealInfo;
            if (!m_pathToSealInfo.TryGetValue(path.Value, out sealInfo))
            {
                yield break;
            }

            int next = sealInfo.HeadIndex;

            while (next >= 0)
            {
                var directoryAndSeal = m_seals.BackingSet[next];
                next = directoryAndSeal.Value.Next;
                yield return new KeyValuePair<DirectoryArtifact, PipId>(directoryAndSeal.Key, directoryAndSeal.Value.PipId);
            }
        }

        /// <summary>
        /// Finds the <see cref="BuildXL.Utilities.DirectoryArtifact"/> for a full <see cref="SealDirectory"/> pip in this collection given a path under the
        /// directory.
        /// Returns <see cref="DirectoryArtifact.Invalid"/> if no full seal has been added.
        /// </summary>
        public DirectoryArtifact TryFindFullySealedDirectoryArtifactForFile(AbsolutePath path)
        {
            foreach (var current in m_pathTable.EnumerateHierarchyBottomUp(path.Value, HierarchicalNameTable.NameFlags.Sealed))
            {
                SealInfo sealInfo;
                if (m_pathToSealInfo.TryGetValue(current, out sealInfo) && sealInfo.FullSealIndex >= 0)
                {
                    return m_seals.BackingSet[sealInfo.FullSealIndex].Key;
                }
            }

            return DirectoryArtifact.Invalid;
        }

        /// <summary>
        /// Finds the <see cref="BuildXL.Utilities.DirectoryArtifact"/> for a full <see cref="SealDirectory"/> pip in this collection given a path under the directory.
        /// </summary>
        /// <param name="directory">Directory to be validated.</param>
        /// <returns>Found directory artifact, otherwise <see cref="DirectoryArtifact.Invalid"/>.</returns>
        public DirectoryArtifact TryFindFullySealedContainingDirectoryArtifact(DirectoryArtifact directory)
        {
            foreach (var current in m_pathTable.EnumerateHierarchyBottomUp(directory.Path.Value, HierarchicalNameTable.NameFlags.Sealed))
            {
                SealInfo sealInfo;
                if (m_pathToSealInfo.TryGetValue(current, out sealInfo) && sealInfo.FullSealIndex >= 0)
                {
                    DirectoryArtifact artifact = m_seals.BackingSet[sealInfo.FullSealIndex].Key;
                    if (!artifact.Equals(directory))
                    {
                        return artifact;
                    }
                }
            }

            return DirectoryArtifact.Invalid;
        }

        /// <summary>
        /// Returns the <see cref="PipId"/> representing a <see cref="SealDirectory"/> corresponding to the given <see cref="DirectoryArtifact"/>.
        /// </summary>
        public bool TryGetSealForDirectoryArtifact(DirectoryArtifact artifact, out PipId pipId)
        {
            Contract.Requires(artifact.IsValid);

            pipId = m_seals.TryGetValue(artifact, out Node node) ? node.PipId : PipId.Invalid;
            return pipId.IsValid;
        }

        /// <summary>
        /// Reserves a new and unique <see cref="BuildXL.Utilities.DirectoryArtifact"/>. This should be assigned to a seal directory pip
        /// with <see cref="SealDirectory.SetDirectoryArtifact"/> before adding it with <see cref="AddSeal"/>.
        /// </summary>
        public DirectoryArtifact ReserveDirectoryArtifact(SealDirectory sealDirectory)
        {
            Contract.Requires(!IsReadOnly);

            // the following 2 preconditions are equivalent to `sealDirectory.IsInitialized == IsPatching`, but are split into 2 to give user friendly messages
            Contract.Requires(!sealDirectory.IsInitialized || IsPatching, "An initialized seal directory may only be added while IsPatching is true");
            Contract.Requires(!IsPatching || sealDirectory.IsInitialized, "During patching, only initialized seal directory may be added");
            Contract.Ensures(Contract.Result<DirectoryArtifact>().Path == sealDirectory.DirectoryRoot);

            if (sealDirectory.IsInitialized)
            {
                return sealDirectory.Directory;
            }
            else
            {
                return CreateDirectoryArtifactWithNewSealId(sealDirectory.DirectoryRoot, isSharedOpaque: false);
            }
        }

        public DirectoryArtifact CreateSharedOpaqueDirectoryWithNewSealId(AbsolutePath directoryRoot)
        {
            return CreateDirectoryArtifactWithNewSealId(directoryRoot, isSharedOpaque: true);
        }

        private DirectoryArtifact CreateDirectoryArtifactWithNewSealId(AbsolutePath directoryRoot, bool isSharedOpaque)
        {
            long sealIndex = Interlocked.Increment(ref m_priorSealIndex);
            Contract.Assume(sealIndex >= 0 && sealIndex <= uint.MaxValue);
            return new DirectoryArtifact(directoryRoot, (uint)sealIndex, isSharedOpaque);
        }

        /// <summary>
        /// Adds a <see cref="SealDirectory"/> with a unique <see cref="BuildXL.Utilities.DirectoryArtifact"/> previously reserved with <see cref="ReserveDirectoryArtifact"/>.
        /// </summary>
        public void AddSeal(SealDirectory seal)
        {
            Contract.Requires(!IsReadOnly);
            Contract.Requires(seal != null);
            Contract.Requires(seal.IsInitialized, "Assign a directory artifact with SetDirectoryArtifact to the pip before adding it");
            Contract.Requires(seal.PipId.IsValid, "SealDirectory pip must be added to the pip table before addition here");

            m_pathToSealInfo.BackingSet.UpdateItem(new UpdateSealItem(this, seal));
        }

        /// <summary>
        /// Transitions this table to <see cref="IsPatching"/> state.
        /// This transition is only valid from <see cref="PatchingNeverStarted"/> state.
        ///
        /// During this state <see cref="ReserveDirectoryArtifact(SealDirectory)"/> accepts
        /// only fully initialized seal directories (<see cref="SealDirectory.IsInitialized"/>).
        /// </summary>
        public void StartPatching()
        {
            Contract.Requires(PatchingNeverStarted, "Multiple patching sessions not allowed");
            Contract.Ensures(IsPatching);
            Contract.Ensures(!PatchingNeverStarted);
            Contract.Ensures(!DonePatching);
            m_patchingState = true;
        }

        /// <summary>
        /// Transitions this table to <see cref="DonePatching"/> state.
        /// This transition is only valid from <see cref="IsPatching"/> state.
        /// </summary>
        /// <remarks>
        /// Not thread safe: don't call this method concurrently with adding
        /// SealDirectories (<see cref="AddSeal(SealDirectory)"/>)
        /// </remarks>
        public void FinishPatching()
        {
            Contract.Requires(IsPatching);
            Contract.Ensures(DonePatching);
            Contract.Ensures(!PatchingNeverStarted);
            Contract.Ensures(!IsPatching);
            m_patchingState = false;
            m_priorSealIndex = m_seals.Count > 0
                ? m_seals.Keys.Max(dir => dir.PartialSealId)
                : 0;
        }

        /// <summary>
        /// Handles updating the seal info for a path
        /// </summary>
        private readonly struct UpdateSealItem : IPendingSetItem<KeyValuePair<HierarchicalNameId, SealInfo>>
        {
            private readonly SealedDirectoryTable m_owner;
            private readonly SealDirectory m_seal;

            public UpdateSealItem(SealedDirectoryTable owner, SealDirectory seal)
            {
                m_owner = owner;
                m_seal = seal;
            }

            public int HashCode => m_seal.DirectoryRoot.Value.GetHashCode();

            public bool Equals(KeyValuePair<HierarchicalNameId, SealInfo> other)
            {
                return m_seal.DirectoryRoot.Value == other.Key;
            }

            /// <summary>
            /// Adds a new head node for the sealed directory at path and
            /// updates flags for path in hierarchical name table
            /// </summary>
            public KeyValuePair<HierarchicalNameId, SealInfo> CreateOrUpdateItem(KeyValuePair<HierarchicalNameId, SealInfo> oldItem, bool hasOldItem, out bool remove)
            {
                remove = false;
                int backingIndex;
                bool added = m_owner.m_seals.TryAdd(m_seal.Directory, new Node(m_seal.PipId, hasOldItem ? oldItem.Value.HeadIndex : -1), out backingIndex);
                Contract.Assume(added, "A reserved DirectoryArtifact should only be used once");

                int fullSealIndex = hasOldItem ? oldItem.Value.FullSealIndex : -1;
                HierarchicalNameTable.NameFlags flags = HierarchicalNameTable.NameFlags.None;
                if (m_seal.Kind != SealDirectoryKind.Partial && m_seal.Kind != SealDirectoryKind.SharedOpaque  && fullSealIndex == -1)
                {
                    fullSealIndex = backingIndex;
                    flags |= HierarchicalNameTable.NameFlags.Sealed;
                }

                if (!hasOldItem)
                {
                    flags |= HierarchicalNameTable.NameFlags.Marked;
                }

                if (flags != HierarchicalNameTable.NameFlags.None)
                {
                    m_owner.m_pathTable.SetFlags(m_seal.DirectoryRoot.Value, flags);
                }

                // We can safely update the head node here because we are in the write lock for this key
                return new KeyValuePair<HierarchicalNameId, SealInfo>(
                    m_seal.DirectoryRoot.Value,
                    new SealInfo(fullSealIndex: fullSealIndex, headIndex: backingIndex));
            }
        }

        /// <summary>
        /// Linked list node for seal directories.
        /// </summary>
        private readonly struct Node
        {
            /// <summary>
            /// The pip id for the pip represented by this node
            /// </summary>
            public readonly PipId PipId;

            /// <summary>
            /// The index of the next node
            /// </summary>
            public readonly int Next;

            public Node(PipId pipId, int next)
            {
                PipId = pipId;
                Next = next;
            }
        }

        /// <summary>
        /// Information about a particular directories seals.
        /// </summary>
        private readonly struct SealInfo
        {
            /// <summary>
            /// The index of the fully sealed directory
            /// </summary>
            public readonly int FullSealIndex;

            /// <summary>
            /// The index of the first seal directory in the linked list for
            /// for this directory path
            /// </summary>
            public readonly int HeadIndex;

            public SealInfo(int fullSealIndex, int headIndex)
            {
                FullSealIndex = fullSealIndex;
                HeadIndex = headIndex;
            }
        }
    }
}
