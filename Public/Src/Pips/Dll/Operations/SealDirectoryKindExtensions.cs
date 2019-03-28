// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Pips.Operations
{
    /// <nodoc/>
    public static class SealDirectoryKindExtensions
    {
        /// <summary>
        /// Whether <paramref name="kind"/> is one of <see cref="SealDirectoryKind.Opaque"/> or <see cref="SealDirectoryKind.SharedOpaque"/>
        /// </summary>
        public static bool IsDynamicKind(this SealDirectoryKind kind)
        {
            switch (kind)
            {
                case SealDirectoryKind.Full:
                case SealDirectoryKind.Partial:
                case SealDirectoryKind.SourceAllDirectories:
                case SealDirectoryKind.SourceTopDirectoryOnly:
                    return false;
                case SealDirectoryKind.Opaque:
                case SealDirectoryKind.SharedOpaque:
                    return true;
                default:
                    throw new NotImplementedException("Unrecognized seal directory kind: " + kind);
            }
        }

        /// <summary>
        /// Whether <paramref name="kind"/> is one of <see cref="SealDirectoryKind.SourceAllDirectories"/> or <see cref="SealDirectoryKind.SourceTopDirectoryOnly"/>
        /// </summary>
        public static bool IsSourceSeal(this SealDirectoryKind kind)
        {
            switch (kind)
            {
                case SealDirectoryKind.Full:
                case SealDirectoryKind.Partial:
                case SealDirectoryKind.Opaque:
                case SealDirectoryKind.SharedOpaque:
                    return false;
                case SealDirectoryKind.SourceAllDirectories:
                case SealDirectoryKind.SourceTopDirectoryOnly:
                    return true;
                default:
                    throw new NotImplementedException("Unrecognized seal directory kind: " + kind);
            }
        }

        /// <summary>
        /// Whether <paramref name="kind"/> is one of <see cref="SealDirectoryKind.Opaque"/> or <see cref="SealDirectoryKind.SharedOpaque"/>
        /// </summary>
        public static bool IsOpaqueOutput(this SealDirectoryKind kind)
        {
            switch (kind)
            {
                case SealDirectoryKind.Full:
                case SealDirectoryKind.Partial:
                case SealDirectoryKind.SourceAllDirectories:
                case SealDirectoryKind.SourceTopDirectoryOnly:
                    return false;
                case SealDirectoryKind.Opaque:
                case SealDirectoryKind.SharedOpaque:
                    return true;
                default:
                    throw new NotImplementedException("Unrecognized seal directory kind: " + kind);
            }
        }

        /// <summary>
        /// Whether <paramref name="kind"/> is <see cref="SealDirectoryKind.Full"/>
        /// </summary>
        public static bool IsFull(this SealDirectoryKind kind) => kind == SealDirectoryKind.Full;

    }
}
