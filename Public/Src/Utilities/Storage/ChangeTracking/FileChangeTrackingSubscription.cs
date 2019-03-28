// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Change subscription for a particular path. This is a token representing membership in a <see cref="FileChangeTrackingSet"/>,
    /// and allows additional tracking of e.g. child path absence (anti-dependencies) and directory membership (enumeration dependencies)
    /// discovered after creation of the subscription.
    /// </summary>
    public readonly struct FileChangeTrackingSubscription : IEquatable<FileChangeTrackingSubscription>
    {
        /// <summary>
        /// Represents the absence of an actual subscription.
        /// </summary>
        public static readonly FileChangeTrackingSubscription Invalid = default;

        /// <summary>
        /// Path to which this subscription applies. This field is internal since a <see cref="FileChangeTrackingSet"/> maintains an internal path table
        /// (so this path is only meaningful to the owning set), and because it is not expected that this type would be serialized and stored (the change
        /// tracking set already stores its set of tracked paths / path table).
        /// </summary>
        internal readonly AbsolutePath ChangeTrackingSetInternalPath;

        /// <nodoc />
        public FileChangeTrackingSubscription(AbsolutePath path) => ChangeTrackingSetInternalPath = path;

        /// <summary>
        /// Indicates if this is a real subscription (not <see cref="Invalid"/>)
        /// </summary>
        public bool IsValid => ChangeTrackingSetInternalPath.IsValid;

        /// <inheritdoc />
        public override string ToString() => I($"[Subscription for path {ChangeTrackingSetInternalPath}]");

        /// <inheritdoc />
        public bool Equals(FileChangeTrackingSubscription other) => other.ChangeTrackingSetInternalPath == ChangeTrackingSetInternalPath;

        /// <inheritdoc />
        public override bool Equals(object obj) => StructUtilities.Equals(this, obj);

        /// <inheritdoc />
        public override int GetHashCode() => ChangeTrackingSetInternalPath.GetHashCode();

        /// <nodoc />
        public static bool operator ==(FileChangeTrackingSubscription left, FileChangeTrackingSubscription right) => left.Equals(right);

        /// <nodoc />
        public static bool operator !=(FileChangeTrackingSubscription left, FileChangeTrackingSubscription right) => !left.Equals(right);
    }
}
