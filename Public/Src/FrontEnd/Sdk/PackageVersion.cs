// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Package version.
    /// </summary>
    public readonly struct PackageVersion : IEquatable<PackageVersion>
    {
        /// <summary>
        /// Min version.
        /// </summary>
        public readonly StringId MinVersion;

        /// <summary>
        /// Max version.
        /// </summary>
        public readonly StringId MaxVersion;

        /// <summary>
        /// Invalid package version.
        /// </summary>
        public static readonly PackageVersion None = new PackageVersion(StringId.Invalid, StringId.Invalid);

        /// <summary>
        /// Constructor.
        /// </summary>
        private PackageVersion(StringId minVersion, StringId maxVersion)
        {
            MinVersion = minVersion;
            MaxVersion = maxVersion;
        }

        /// <summary>
        /// Creates a package version.
        /// </summary>
        public static PackageVersion Create(StringId minVersion, StringId maxVersion)
        {
            Contract.Requires(minVersion.IsValid);
            Contract.Requires(maxVersion.IsValid);

            return new PackageVersion(minVersion, maxVersion);
        }

        /// <summary>
        /// Creates a package version.
        /// </summary>
        public static PackageVersion Create(StringTable stringTable, string minVersion, string maxVersion)
        {
            Contract.Requires(stringTable != null);
            Contract.Requires(minVersion != null);
            Contract.Requires(maxVersion != null);

            return new PackageVersion(StringId.Create(stringTable, minVersion), StringId.Create(stringTable, maxVersion));
        }

        /// <inheritdoc />
        public bool Equals(PackageVersion other)
        {
            return MinVersion == other.MinVersion && MaxVersion == other.MaxVersion;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(MinVersion.Value, MaxVersion.Value);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToString(null);
        }

        /// <summary>
        /// Gets the string representation.
        /// </summary>
        [Pure]
        public string ToString(StringTable stringTable)
        {
            if (stringTable == null || this == None)
            {
                return string.Empty;
            }

            if (MinVersion == MaxVersion)
            {
                return MinVersion.ToString(stringTable);
            }

            return MinVersion.ToString(stringTable) + "---" + MaxVersion.ToString(stringTable);
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(PackageVersion left, PackageVersion right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(PackageVersion left, PackageVersion right)
        {
            return !left.Equals(right);
        }
    }
}
