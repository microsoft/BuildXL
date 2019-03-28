// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Storage
{
    /// <summary>
    /// Pair of a <see cref="VersionedFileIdentity" /> and a known <see cref="FileContentInfo" /> (hash and size) at that version.
    /// </summary>
    public readonly struct VersionedFileIdentityAndContentInfo : IEquatable<VersionedFileIdentityAndContentInfo>
    {
        /// <summary>
        /// File identity including a version number.
        /// </summary>
        public readonly VersionedFileIdentity Identity;

        /// <summary>
        /// Content hash and length for the identified file.
        /// </summary>
        public readonly FileContentInfo FileContentInfo;

        /// <nodoc />
        public VersionedFileIdentityAndContentInfo(VersionedFileIdentity identity, FileContentInfo fileContentInfo)
        {
            Identity = identity;
            FileContentInfo = fileContentInfo;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"[File {Identity} with content {FileContentInfo}]");
        }

        /// <inheritdoc />
        public bool Equals(VersionedFileIdentityAndContentInfo other)
        {
            return other.FileContentInfo == FileContentInfo && other.Identity == Identity;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(FileContentInfo.GetHashCode(), Identity.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(VersionedFileIdentityAndContentInfo left, VersionedFileIdentityAndContentInfo right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(VersionedFileIdentityAndContentInfo left, VersionedFileIdentityAndContentInfo right)
        {
            return !left.Equals(right);
        }
    }
}
