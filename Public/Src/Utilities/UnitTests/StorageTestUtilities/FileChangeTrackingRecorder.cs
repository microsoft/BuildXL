// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace Test.BuildXL.StorageTestUtilities
{
    /// <summary>
    /// Mock change tracker which records the set of file identities for which tracking was requested.
    /// </summary>
    public sealed class FileChangeTrackingRecorder : IFileChangeTrackingSubscriptionSource
    {
        private readonly object m_lock = new object();
        private readonly bool m_verifyKnownIdentity;

        public readonly HashSet<string> PathsWithTrackedExistence = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> PathsWithTrackedMembership = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<VersionedFileIdentity> TrackedFileIdentities = new HashSet<VersionedFileIdentity>();

        public FileChangeTrackingRecorder(bool verifyKnownIdentity)
        {
            m_verifyKnownIdentity = verifyKnownIdentity;
        }

        public FileChangeTrackingSubscription TryTrackChangesToFile(
            SafeFileHandle handle,
            string path,
            VersionedFileIdentity? maybeIdentity = null,
            TrackingUpdateMode updateMode = TrackingUpdateMode.Preserve)
        {
            Contract.Requires(handle != null);
            Contract.Requires(path != null);

            Possible<VersionedFileIdentity, Failure<VersionedFileIdentity.IdentityUnavailabilityReason>> possibleIdentity =
                VersionedFileIdentity.TryQuery(handle);
            if (!possibleIdentity.Succeeded)
            {
                throw possibleIdentity.Failure.Throw();
            }

            VersionedFileIdentity realIdentity = possibleIdentity.Result;

            if (m_verifyKnownIdentity &&
                maybeIdentity.HasValue &&
                maybeIdentity.Value.Kind.IsWeakOrStrong())
            {
                XAssert.AreEqual(realIdentity.FileId, maybeIdentity.Value.FileId);
                XAssert.AreEqual(realIdentity.Usn, maybeIdentity.Value.Usn);
            }

            // Note that maybeIdentity may be used here even if it is Anonymous
            // This is the case for some tests that pretend file versions are unavailable.
            return TryTrackChangesToFile(maybeIdentity ?? realIdentity);
        }

        private FileChangeTrackingSubscription TryTrackChangesToFile(VersionedFileIdentity identity)
        {
            lock (m_lock)
            {
                if (identity.Kind.IsWeakOrStrong())
                {
                    TrackedFileIdentities.Add(identity.ToWeakIdentity());
                    return new FileChangeTrackingSubscription(new AbsolutePath(1));
                }
                else
                {
                    return FileChangeTrackingSubscription.Invalid;
                }
            }
        }

        public Possible<FileChangeTrackingSet.EnumerationResult> TryEnumerateDirectoryAndTrackMembership(
            string path,
            Action<string, FileAttributes> handleEntry)
        {
            Contract.Requires(path != null);
            Contract.Requires(handleEntry != null);

            var possibleFingerprintResult = DirectoryMembershipTrackingFingerprinter.ComputeFingerprint(path, handleEntry);

            if (!possibleFingerprintResult.Succeeded)
            {
                return possibleFingerprintResult.Failure;
            }

            PathExistence existence = possibleFingerprintResult.Result.PathExistence;
            Possible<FileChangeTrackingSet.ConditionalTrackingResult> trackingResult = new Failure<string>("Tracking membership failed");

            lock (m_lock)
            {
                if (existence == PathExistence.ExistsAsDirectory)
                {
                    trackingResult = FileChangeTrackingSet.ConditionalTrackingResult.Tracked;
                    PathsWithTrackedMembership.Add(Path.GetFullPath(path));
                }

                PathsWithTrackedExistence.Add(Path.GetFullPath(path));
            }

            return new FileChangeTrackingSet.EnumerationResult(
                possibleFingerprintResult.Result.Fingerprint,
                possibleFingerprintResult.Result.PathExistence,
                trackingResult);
        }

        public Possible<PathExistence> TryProbeAndTrackPath(string path, bool? isReadOnly = default)
        {
            Contract.Requires(path != null);

            PathExistence existence = GetExistence(path);

            lock (m_lock)
            {
                PathsWithTrackedExistence.Add(Path.GetFullPath(path));
            }

            return existence;
        }

        private static PathExistence GetExistence(string path)
        {
            if (File.Exists(path))
            {
                return PathExistence.ExistsAsFile;
            }
            else if (Directory.Exists(path))
            {
                return PathExistence.ExistsAsDirectory;
            }
            else
            {
                return PathExistence.Nonexistent;
            }
        }

        public void AssertExistenceOfPathIsTracked(string path)
        {
            XAssert.IsTrue(
                PathsWithTrackedExistence.Contains(Path.GetFullPath(path)),
                "Expecting existence of path to be tracked (via TryProbeAndTrackPath): {0}",
                path);
        }

        public void AssertExistenceOfPathIsNotTracked(string path)
        {
            XAssert.IsFalse(
                PathsWithTrackedExistence.Contains(Path.GetFullPath(path)),
                "Expecting existence of path to not be tracked (but TryProbeAndTrackPath was called on it): {0}",
                path);
        }

        public void AssertMembershipOfPathIsTracked(string path)
        {
            XAssert.IsTrue(
                PathsWithTrackedExistence.Contains(Path.GetFullPath(path)),
                "Expecting directory-membership of path to be tracked (via TryEnumerateDirectoryAndTrackMembership): {0}",
                path);
        }

        public void AssertMembershipOfPathIsNotTracked(string path)
        {
            XAssert.IsFalse(
                PathsWithTrackedExistence.Contains(Path.GetFullPath(path)),
                "Expecting directory-membership of path to not be tracked (but TryEnumerateDirectoryAndTrackMembership was called): {0}",
                path);
        }

        public void AssertPathIsTracked(string path)
        {
            using (FileStream fileStream = File.OpenRead(path))
            {
                Possible<VersionedFileIdentity, Failure<VersionedFileIdentity.IdentityUnavailabilityReason>> possibleIdentity =
                    VersionedFileIdentity.TryQuery(fileStream.SafeFileHandle);
                if (!possibleIdentity.Succeeded)
                {
                    throw possibleIdentity.Failure.Throw();
                }

                VersionedFileIdentity identity = possibleIdentity.Result;
                XAssert.IsTrue(
                    TrackedFileIdentities.Contains(identity.ToWeakIdentity()),
                    "Expected path {0} (current identity {1}) to be tracked for changes",
                    path,
                    identity);
            }
        }

        public void AssertPathIsNotTracked(string path)
        {
            using (FileStream fileStream = File.OpenRead(path))
            {
                Possible<VersionedFileIdentity, Failure<VersionedFileIdentity.IdentityUnavailabilityReason>> possibleIdentity =
                    VersionedFileIdentity.TryQuery(fileStream.SafeFileHandle);
                if (!possibleIdentity.Succeeded)
                {
                    throw possibleIdentity.Failure.Throw();
                }

                VersionedFileIdentity identity = possibleIdentity.Result;
                XAssert.IsFalse(
                    TrackedFileIdentities.Contains(identity.ToWeakIdentity()),
                    "Expected path {0} (current identity {1}) to not be tracked for changes",
                    path,
                    identity);
            }
        }

        public bool TrackAbsentRelativePath([JetBrains.Annotations.NotNull] string trackedParentPath, [JetBrains.Annotations.NotNull] string relativeAbsentPath)
        {
            return false;
        }
    }
}
