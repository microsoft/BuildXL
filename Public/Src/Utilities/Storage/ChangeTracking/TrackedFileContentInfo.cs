// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// A <see cref="TrackedFileContentInfo"/> is the combination of a file's known content hash at some instant,
    /// and a handle representing notification of that file's eventual change (in a <see cref="FileChangeTrackingSet"/>).
    /// </summary>
    public readonly struct TrackedFileContentInfo : IEquatable<TrackedFileContentInfo>
    {
        /// <summary>
        /// Subscription representing membership in a <see cref="FileChangeTrackingSet"/>.
        /// </summary>
        /// <remarks>
        /// TODO: Eventually, this may allow cancellation of tracking.
        /// </remarks>
        public readonly FileChangeTrackingSubscription Subscription;

        /// <summary>
        /// Underlying <see cref="FileContentInfo"/> (hash and length of the corresponding file).
        /// Changes to these values on disk may be later detected due to the change-tracking <see cref="Subscription"/>.
        /// </summary>
        public readonly FileContentInfo FileContentInfo;

        /// <summary>
        /// Optional file name path atom specifying file name with appropriate casing
        /// </summary>
        public readonly PathAtom FileName;

        /// <summary>
        /// Type and target (if available) of the reparse point
        /// </summary>
        public readonly ReparsePointInfo ReparsePointInfo;

        /// <summary>
        /// Gets the file materialization info
        /// </summary>
        public FileMaterializationInfo FileMaterializationInfo => new FileMaterializationInfo(FileContentInfo, FileName, ReparsePointInfo);

        /// <summary>
        /// Creates a <see cref="TrackedFileContentInfo"/> with an associated change tracking subscription.
        /// </summary>
        public TrackedFileContentInfo(FileContentInfo fileContentInfo, FileChangeTrackingSubscription subscription, PathAtom fileName, ReparsePointInfo? reparsePointInfo = null)
        {
            Subscription = subscription;
            FileContentInfo = fileContentInfo;
            FileName = fileName;
            ReparsePointInfo = reparsePointInfo ?? ReparsePointInfo.CreateNoneReparsePoint();
        }

        /// <summary>
        /// Creates a <see cref="TrackedFileContentInfo"/> with a hash but no tracking information.
        /// This is intended for when change tracking is disable or unsupported (due to e.g. a volume having change journaling disabled).
        /// </summary>
        public static TrackedFileContentInfo CreateUntracked(FileContentInfo fileContentInfo, PathAtom fileName = default)
        {
            Contract.Ensures(!Contract.Result<TrackedFileContentInfo>().IsTracked);

            return new TrackedFileContentInfo(fileContentInfo, FileChangeTrackingSubscription.Invalid, fileName);
        }

        /// <summary>
        /// Creates a <see cref="TrackedFileContentInfo"/> with a hash but no tracking information and no known length.
        /// This is intended for abstract hashes that don't correspond to real files on disk.
        /// </summary>
        public static TrackedFileContentInfo CreateUntrackedWithUnknownLength(ContentHash hash, PathExistence? existence = null)
        {
            Contract.Ensures(!Contract.Result<TrackedFileContentInfo>().IsTracked);

            return new TrackedFileContentInfo(FileContentInfo.CreateWithUnknownLength(hash, existence), FileChangeTrackingSubscription.Invalid, PathAtom.Invalid);
        }

        /// <summary>
        /// Indicates if changes to this file are actually tracked (otherwise it was created via <c>CreateUntracked"</c>).
        /// </summary>
        public bool IsTracked => Subscription.IsValid;

        /// <summary>
        /// Content hash of the file as of when tracking was started.
        /// </summary>
        public ContentHash Hash => FileContentInfo.Hash;

        /// <summary>
        /// Length of the file in bytes.
        /// </summary>
        public long Length => FileContentInfo.Length;

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"[Tracking content {FileContentInfo} via subscription {Subscription}]");
        }

        /// <inheritdoc />
        public bool Equals(TrackedFileContentInfo other)
        {
            return other.Subscription == Subscription &&
                   other.FileContentInfo == FileContentInfo &&
                   other.ReparsePointInfo == ReparsePointInfo;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(FileContentInfo.GetHashCode(), Subscription.GetHashCode(), ReparsePointInfo.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(TrackedFileContentInfo left, TrackedFileContentInfo right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(TrackedFileContentInfo left, TrackedFileContentInfo right)
        {
            return !left.Equals(right);
        }
    }
}
