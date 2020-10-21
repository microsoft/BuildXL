// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Accepts files to track and returns <see cref="FileChangeTrackingSubscription"/>s corresponding to them.
    /// </summary>
    /// <remarks>
    /// Many operations need to track the files that they access, but do not themselves scan for changes to files.
    /// Such operations can take a <see cref="IFileChangeTrackingSubscriptionSource"/> rather than a full-fledged <see cref="FileChangeTracker"/>,
    /// </remarks>
    public interface IFileChangeTrackingSubscriptionSource
    {
        /// <summary>
        /// Attempts to add the provided file to the change tracking set. <paramref name="maybeIdentity"/> (if provided)
        /// must correspond to the file identity of <paramref name="handle"/>.
        /// See <see cref="FileChangeTrackingSet.TryTrackChangesToFile"/>.
        /// If the file cannot be tracked, an invalid subscription is returned.
        /// </summary>
        /// <remarks>
        /// We allow providing an identity to avoid querying identity twice (files are often also added to a <see cref="FileContentTable"/>,
        /// which also needs an identity).
        /// </remarks>
        FileChangeTrackingSubscription TryTrackChangesToFile(
            [NotNull]SafeFileHandle handle,
            [NotNull]string path,
            VersionedFileIdentity? maybeIdentity = null,
            TrackingUpdateMode updateMode = TrackingUpdateMode.Preserve);

        /// <summary>
        /// Combined operation of opening and tracking a directory (or its absence), enumerating it, and then tracking changes to that enumeration result (its membership).
        /// The membership of the directory will be invalidated if a name is added or removed directly inside the directory (i.e., when <c>FindFirstFile</c>
        /// and <c>FindNextFile</c> would see a different set of names).
        /// </summary>
        /// <remarks>
        /// <paramref name="handleEntry"/> will only be called on an entry if <paramref name="shouldIncludeEntry"/> on that entry returns true.
        /// <paramref name="supersedeWithStrongIdentity"/> is used to supersede the tracked USN of <paramref name="path"/> with the max USN of
        /// its entries that are included by <paramref name="shouldIncludeEntry"/>. This is essential for tracking output directory.
        /// 
        /// Suppose that we have the following output directory:
        /// D\
        ///   1.txt
        ///   2.txt
        ///   3.txt
        /// When we produce D, we track D and record its membership fingerprint. By tracking, D is recorded with some USN-D.
        /// The issue is D's members are typically created after D. Thus, each of them has higher USN, and that USN contains CREATED change reason.
        /// When we scan our journal later we find this higher USN containing CREATED, and journal processing thinks that D's membership
        /// has probably changed. Our journal scanning then needs to work extra to prove that the membership has not changed by enumerating D again
        /// and compare its recorded fingerprint with the current one. See CheckAndMaybeInvalidateEnumerationDependencies in FileChangeTrackingSet.cs
        /// for details.
        /// 
        /// When <paramref name="supersedeWithStrongIdentity"/> is true, after the enumeration, we retrack again D by establishing a strong
        /// identity for it, i.e., we create a dummy CLOSE record so that D will have higher USN than all of its members. Then, we use that higher
        /// USN as D's supersession limit. Thus, any membership change below that higher USN will be ignored during the journal scanning.
        /// 
        /// One can think what about if D is superseded by max USN among its members. First, one needs to query the USNs of D's members, and it
        /// affects performance of enumeration. Second, the enumeratioon only care about the member included by <paramref name="shouldIncludeEntry"/>.
        /// The excluded member may have higher USN then any included members'.
        /// </remarks>
        Possible<FileChangeTrackingSet.EnumerationResult> TryEnumerateDirectoryAndTrackMembership(
            [NotNull]string path,
            [NotNull]Action<string, FileAttributes> handleEntry,
            Func<string, FileAttributes, bool> shouldIncludeEntry,
            bool supersedeWithStrongIdentity);

        /// <summary>
        /// Probes for the existence of a path, while also tracking the result (e.g. if a file does not exist and is later created, that change will be detected).
        /// If probing succeeds but tracking fails, a <see cref="PathExistence"/> is still returned (the underlying tracker should record that tracking is incomplete).
        /// </summary>
        Possible<PathExistence> TryProbeAndTrackPath([NotNull]string path, bool? isReadOnly = default);

        /// <summary>
        /// Tracks a non-existent relative path chain from a tracked parent root.
        /// If trackedParentPath = 'C:\foo' and relativeAbsentPath = 'a\b\c'
        /// Then the following paths are tracked as absent: 'C:\foo\a', 'C:\foo\a\b', 'C:\foo\a\b\c'.
        /// ADVANCED. Use with care. This should only because if the relative has been guaranteed to be non-existent
        /// because the parent path non-existent or enumerated and the child path was non-existent
        /// </summary>
        bool TrackAbsentRelativePath([NotNull]string trackedParentPath, [NotNull]string relativeAbsentPath);
    }
}
