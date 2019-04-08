// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Storage.ChangeJournalService.Protocol;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Execution.Analyzer.Analyzers
{
    /// <summary>
    /// Maps a set of GIT changes into an observable for <see cref="IObserver<ChangedFileIdInfo>"/>
    /// </summary>
    public class GitFileChangeObservable : IFileChangeTrackingObservable
    {
        /// <summary>
        /// All changes
        /// </summary>
        public List<ChangedPathInfo> Changes { get; }

        private PathTable m_pathTable;

        /// <summary>
        /// An observable with no changes. Mainly used for testing purposes.
        /// </summary>
        public static GitFileChangeObservable NoChanges() => new GitFileChangeObservable();

        /// <nodoc/>
        public GitFileChangeObservable(GitTreeDiffResponse changes, PathTable pathTable)
        {
            Contract.Requires(changes != null);
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
            Changes = ComputeChangedPathInfo(changes);
        }

        private GitFileChangeObservable()
        {
            Changes = new List<ChangedPathInfo>();
        }

        private List<ChangedPathInfo> ComputeChangedPathInfo(GitTreeDiffResponse changes)
        {
            var result = new List<ChangedPathInfo>();

            // Renames and object type changes are returned as delete on the old object and add on the new object.
            // So we compute the set of changes for a given path to determine and report type changes
            var objectTypePerPath = new Dictionary<string, List<GitObjectType>>();

            foreach (var change in changes.TreeDiff.DiffEntries)
            {
                if (objectTypePerPath.TryGetValue(change.Path, out var objectTypes))
                {
                    objectTypes.Add(change.ObjectType);
                }
                else
                {
                    objectTypePerPath[change.Path] = new List<GitObjectType> { change.ObjectType };
                }
            }

            foreach (var change in changes.TreeDiff.DiffEntries)
            {
                PathChanges pathChanges;
                switch (change.ChangeType)
                {
                    case VersionControlChangeType.Add:
                        pathChanges = PathChanges.NewlyPresent;
                        break;
                    case VersionControlChangeType.Edit:
                        pathChanges = PathChanges.DataOrMetadataChanged;
                        break;
                    case VersionControlChangeType.Delete:
                        pathChanges = PathChanges.Removed;
                        break;
                    default:
                        throw new NotImplementedException(I($"Change kind '{change.ChangeType}' is not supported"));
                }

                // Analyze the type changes now to detect file -> directory and directory -> file
                var objectTypes = objectTypePerPath[change.Path];
                Contract.Assert(objectTypes.Count > 0);

                if (objectTypes.Count > 1)
                {
                    var firstType = objectTypes[0];
                    var lastType = objectTypes[objectTypes.Count - 1];

                    if (firstType == GitObjectType.Blob && lastType == GitObjectType.Tree)
                    {
                        pathChanges |= PathChanges.NewlyPresentAsDirectory;
                    }
                    else if (firstType == GitObjectType.Tree && lastType == GitObjectType.Blob)
                    {
                        pathChanges |= PathChanges.NewlyPresentAsFile;
                    }
                }

                // This assumes BuildXL ran with subst.
                // TODO: revisit
                result.Add(new ChangedPathInfo("B:" + change.Path, pathChanges));

                // Now trigger membership changes events if the type of change was add or delete
                if (change.ChangeType == VersionControlChangeType.Add || change.ChangeType == VersionControlChangeType.Delete)
                {
                    AddAllAssociatedMemebershipChanges(change.Path, result);
                }
            }

            return result;
        }

        private void AddAllAssociatedMemebershipChanges(string path, List<ChangedPathInfo> result)
        {
            var absolutePath = AbsolutePath.Create(m_pathTable, "B:" + path);

            // Report fingerprint changes for all paths upstream
            var parent = absolutePath.GetParent(m_pathTable);
            while (parent.IsValid)
            {
                result.Add(new ChangedPathInfo(parent.ToString(m_pathTable), PathChanges.MembershipChanged));
                parent = parent.GetParent(m_pathTable);
            }
        }

        public IDisposable Subscribe(IObserver<ChangedPathInfo> observer)
        {
            foreach (var change in this.Changes)
            {
                observer.OnNext(change);
            }

            observer.OnCompleted();

            return null;
        }

        /// <summary>
        /// Ignored for now.
        /// </summary>
        public IDisposable Subscribe(IObserver<ChangedFileIdInfo> observer) => null;

        /// <inheritdoc/>
        public IDisposable Subscribe(IFileChangeTrackingObserver observer)
        {
            Subscribe((IObserver<ChangedPathInfo>) observer);
            observer.OnCompleted(ScanningJournalResult.Success(new CounterCollection<ReadJournalCounter>()));

            return null;
        }

        /// <summary>
        /// Always invalid
        /// </summary>
        public FileEnvelopeId FileEnvelopeId => FileEnvelopeId.Invalid;
    }
}

