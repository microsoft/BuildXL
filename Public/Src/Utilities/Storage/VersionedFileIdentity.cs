// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Storage
{
    /// <summary>
    /// Machine-global identity of a file at some particular version.
    /// </summary>
    /// <remarks>
    /// An identity derived from change-journal USNs may be 'strong' or 'weak'.
    /// The same weak identity may be returned for multiple actual versions of a file,
    /// since consecutive changes to a file may share a single change record whereas a 'strong' identity is guaranteed to change.
    /// (expected pattern is to compare weak identities with strong ones obtained with <see cref="TryEstablishStrong" />).
    /// Establishing a weak identity is typically less expensive.
    /// See https://msdn.microsoft.com/en-us/library/windows/desktop/aa363803(v=vs.85).aspx for an explanation of how file changes do not always
    /// change its weak identity.
    /// TODO: Strong vs. weak identities are distinguished via a <see cref="IdentityKind"/>. In the future, there may be a new kind for timestamp-based identity;
    ///       it is useful to establish file identities even where change journals are disabled or unsupported.
    /// </remarks>
    public readonly struct VersionedFileIdentity : IEquatable<VersionedFileIdentity>
    {
        /// <summary>
        /// Canonical empty identity with kind <see cref="IdentityKind.Anonymous"/>.
        /// </summary>
        public static readonly VersionedFileIdentity Anonymous = default;

        /// <summary>
        /// Indicates if this identity is strong (definitely will change if the file changes) or weak (may change if the file changes).
        /// </summary>
        public readonly IdentityKind Kind;

        private readonly FileId m_fileId;

        private readonly ulong m_volumeSerialNumber;

        private readonly Usn m_usn;

        /// <summary>
        /// Indicates if this identity has kind <see cref="IdentityKind.Anonymous"/>,
        /// in which case identity fields like <see cref="FileId"/> are unavailable.
        /// </summary>
        public bool IsAnonymous => Kind.IsAnonymous();

        private static Failure<string> s_notSupportedInnerFailure = OperatingSystemHelper.IsUnixOS
            ? null
            : new Failure<string>("Change journal in the volume is disabled or is not supported");

        /// <summary>
        /// ID of the file on its volume (unique only within a volume)
        /// </summary>
        public FileId FileId
        {
            get
            {
                Contract.Requires(!IsAnonymous);
                return m_fileId;
            }
        }

        /// <summary>
        /// ID of the volume (unique only on a machine)
        /// </summary>
        public ulong VolumeSerialNumber
        {
            get
            {
                Contract.Requires(!IsAnonymous);
                return m_volumeSerialNumber;
            }
        }

        /// <summary>
        /// Version number (changes under file modification; unique per volume)
        /// </summary>
        public Usn Usn
        {
            get
            {
                Contract.Requires(!IsAnonymous);
                return m_usn;
            }
        }

        /// <summary>
        /// Constructor for USN-based identity.
        /// </summary>
        public VersionedFileIdentity(ulong volumeSerialNumber, FileId fileId, Usn usn, IdentityKind kind)
        {
            Contract.Requires(kind.IsWeakOrStrong());

            m_fileId = fileId;
            m_volumeSerialNumber = volumeSerialNumber;
            m_usn = usn;
            Kind = kind;
        }

        /// <summary>
        /// Failure reason for <see cref="VersionedFileIdentity.TryQuery" /> or <see cref="VersionedFileIdentity.TryEstablishStrong"/>
        /// </summary>
        public enum IdentityUnavailabilityReason
        {
            /// <summary>
            /// General failure querying identity. See inner failure.
            /// </summary>
            Unknown,

            /// <summary>
            /// Querying identity is not supported, e.g., the change journal in NTFS is disabled on the file's volume.
            /// </summary>
            NotSupported,
        }

        /// <summary>
        /// Represents the various means by which file identity can be established (e.g. timestamps vs. USNs).
        /// </summary>
        public enum IdentityKind
        {
            /// <summary>
            /// The absence of any identity. This may occur when a desired form of identity (e.g., USN-based or timestamp) is not available for a file.
            /// </summary>
            Anonymous,

            /// <summary>
            /// This identity may change if the file changes.
            /// </summary>
            /// <remarks>
            /// In file system that supports journaling (e.g., NTFS), the same weak identity may be returned for multiple actual versions of a file,
            /// since consecutive changes to a file may share a single change record whereas a 'strong' identity is guaranteed to change.
            /// (expected pattern is to compare weak identities with strong ones).
            /// </remarks>
            WeakUsn,

            /// <summary>
            /// This identity definitely will change if the file changes.
            /// </summary>
            StrongUsn,
        }

        /// <summary>
        /// Queries a weak version-identity for the given file handle.
        /// </summary>
        /// <param name="handle">File handle.</param>
        /// <returns>A version identity if successful, otherwise a failure.</returns>
        /// <remarks>
        /// On Windows, the same weak identity may be returned for multiple actual versions of a file, since consecutive changes to a file may share a single change record
        /// (expected pattern is to compare weak identities with strong ones obtained with <see cref="TryEstablishStrong" />).
        /// Establishing a weak identity is typically less expensive.
        /// For Windows, wee https://msdn.microsoft.com/en-us/library/windows/desktop/aa363803(v=vs.85).aspx for an explanation of how file changes do not always
        /// change its weak identity.
        /// </remarks>
        public static Possible<VersionedFileIdentity, Failure<IdentityUnavailabilityReason>> TryQuery(SafeFileHandle handle)
        {
            Contract.Requires(handle != null);
            Contract.Ensures(
                !Contract.Result<Possible<VersionedFileIdentity, Failure<IdentityUnavailabilityReason>>>().Succeeded
                || Contract.Result<Possible<VersionedFileIdentity, Failure<IdentityUnavailabilityReason>>>().Result.Kind == IdentityKind.WeakUsn);

            (FileIdAndVolumeId fileIdentity, Usn usn)? versionedFileIdentity = FileUtilities.TryGetVersionedFileIdentityByHandle(handle);

            if (!versionedFileIdentity.HasValue)
            {
                return new Failure<IdentityUnavailabilityReason>(IdentityUnavailabilityReason.NotSupported, s_notSupportedInnerFailure);
            }

            return new VersionedFileIdentity(
                versionedFileIdentity.Value.fileIdentity.VolumeSerialNumber,
                versionedFileIdentity.Value.fileIdentity.FileId,
                versionedFileIdentity.Value.usn,
                kind: IdentityKind.WeakUsn);
        }

        /// <summary>
        /// Establishes a strong identity for the given file handle.
        /// </summary>
        /// <param name="handle">File handle.</param>
        /// <param name="flush">Whether or not to flush file system page cache.</param>
        /// <returns>A version identity if successful, otherwise a failure.</returns>
        /// <remarks>
        /// This identity will definitely change if the file is modified, and so is useful to store for comparison 
        /// later (expected pattern is to compare weak identities obtained with <see cref="TryQuery" />
        /// to known strong identities). Establishing a strong identity involves a write to the volume's change journal, and so is more expensive.
        /// The <paramref name="flush" /> parameter, when set flushes dirtied cache pages have been handed back to the filesystem. This requires that the
        /// stream has been opened writable. On Windows, this flag is needed if the content has been recently written via a memory mapping;
        /// otherwise the USN for the file may change again shortly after this call due to lazy cache write-back.
        /// </remarks>
        public static Possible<VersionedFileIdentity, Failure<IdentityUnavailabilityReason>> TryEstablishStrong(
            SafeFileHandle handle,
            bool flush = false)
        {
            Contract.Ensures(
                !Contract.Result<Possible<VersionedFileIdentity, Failure<IdentityUnavailabilityReason>>>().Succeeded
                || Contract.Result<Possible<VersionedFileIdentity, Failure<IdentityUnavailabilityReason>>>().Result.Kind == IdentityKind.StrongUsn);

            (FileIdAndVolumeId fileIdentity, Usn usn)? versionedFileIdentity = FileUtilities.TryEstablishVersionedFileIdentityByHandle(handle, flush);

            if (!versionedFileIdentity.HasValue)
            {
                return new Failure<IdentityUnavailabilityReason>(IdentityUnavailabilityReason.NotSupported, s_notSupportedInnerFailure);
            }

            return new VersionedFileIdentity(
                versionedFileIdentity.Value.fileIdentity.VolumeSerialNumber,
                versionedFileIdentity.Value.fileIdentity.FileId,
                versionedFileIdentity.Value.usn,
                kind: IdentityKind.StrongUsn);
        }

        /// <summary>
        /// Checks if a handle has a precise file version.
        /// </summary>
        /// <param name="handle">File handle.</param>
        /// <returns>True if the file has a precise file version.</returns>
        public static bool HasPreciseFileVersion(SafeFileHandle handle) => FileUtilities.CheckIfVolumeSupportsPreciseFileVersionByHandle(handle);

        /// <summary>
        /// Checks if a file has a precise file version.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <returns>True if the file has a precise file version.</returns>
        public static bool HasPreciseFileVersion(string path)
        {
            using (FileStream stream = FileUtilities.CreateFileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                return HasPreciseFileVersion(stream.SafeFileHandle);
            }
        }

        /// <summary>
        /// Demotes this identity to be weak. This is useful for strength-independent comparisons.
        /// The identity must be <see cref="IdentityKind.WeakUsn"/> or <see cref="IdentityKind.StrongUsn"/>
        /// </summary>
        [Pure]
        public VersionedFileIdentity ToWeakIdentity()
        {
            Contract.Requires(Kind.IsWeakOrStrong());
            Contract.Requires(!IsAnonymous);

            return new VersionedFileIdentity(VolumeSerialNumber, FileId, Usn, kind: IdentityKind.WeakUsn);
        }

        /// <inheritdoc />
        public bool Equals(VersionedFileIdentity other)
        {
            return other.m_volumeSerialNumber == m_volumeSerialNumber &&
                   other.m_usn == m_usn &&
                   other.m_fileId == m_fileId &&
                   other.Kind == Kind;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return IsAnonymous
                ? I($"[{Kind.ToKindString()} identity]")
                : I($"[{Kind.ToKindString()} identity for {FileId} on volume {VolumeSerialNumber:X16} @ {Usn.ToString()}]");
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(m_volumeSerialNumber.GetHashCode(), m_fileId.GetHashCode(), m_usn.GetHashCode(), Kind.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(VersionedFileIdentity left, VersionedFileIdentity right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(VersionedFileIdentity left, VersionedFileIdentity right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Extension class for <see cref="VersionedFileIdentity.IdentityKind"/>.
    /// </summary>
    public static class IdentityKindExtension
    {
        /// <summary>
        /// Checks if the kind is <see cref="VersionedFileIdentity.IdentityKind.Anonymous"/>.
        /// </summary>
        public static bool IsAnonymous(this VersionedFileIdentity.IdentityKind kind) => kind == VersionedFileIdentity.IdentityKind.Anonymous;

        /// <summary>
        /// Checks if the kind is <see cref="VersionedFileIdentity.IdentityKind.WeakUsn"/> or <see cref="VersionedFileIdentity.IdentityKind.StrongUsn"/>.
        /// </summary>
        public static bool IsWeakOrStrong(this VersionedFileIdentity.IdentityKind kind) => kind == VersionedFileIdentity.IdentityKind.WeakUsn || kind == VersionedFileIdentity.IdentityKind.StrongUsn;

        /// <summary>
        /// Gets a string representation of <see cref="VersionedFileIdentity.IdentityKind"/>.
        /// </summary>
        public static string ToKindString(this VersionedFileIdentity.IdentityKind kind)
        {
            switch (kind)
            {
                case VersionedFileIdentity.IdentityKind.Anonymous:
                    return "Anonymous";
                case VersionedFileIdentity.IdentityKind.StrongUsn:
                    return "Strong";
                case VersionedFileIdentity.IdentityKind.WeakUsn:
                    return "Weak";
                default:
                    throw Contract.AssertFailure(I($"Unknown identity kind {kind}"));
            }
        }
    }
}
