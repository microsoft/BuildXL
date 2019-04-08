// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Package id key for environment.
    /// </summary>
    public readonly struct PackageId : IEquatable<PackageId>
    {
        /// <summary>
        /// Package name.
        /// </summary>
        public readonly StringId Name;

        /// <summary>
        /// Package version.
        /// </summary>
        public readonly PackageVersion Version;

        /// <summary>
        /// Invalid package id.
        /// </summary>
        public static readonly PackageId Invalid = new PackageId(StringId.Invalid, PackageVersion.None);

        /// <summary>
        /// Constructor.
        /// </summary>
        private PackageId(StringId name, PackageVersion version)
        {
            Name = name;
            Version = version;
        }

        /// <summary>
        /// Creates a package id.
        /// </summary>
        public static PackageId Create(StringId name)
        {
            Contract.Requires(name.IsValid, "Package name should be valid.");
            return new PackageId(name, PackageVersion.None);
        }

        /// <summary>
        /// Creates a package id.
        /// </summary>
        public static PackageId Create(StringId name, PackageVersion version)
        {
            Contract.Requires(name.IsValid, "Package name should be valid.");
            Contract.Requires(version != PackageVersion.None, "Package version should not be 'PackageVersion.None'.");

            return new PackageId(name, version);
        }

        /// <summary>
        /// Creates a package id.
        /// </summary>
        public static PackageId Create(StringTable stringTable, string name)
        {
            Contract.Requires(stringTable != null, "stringTable != null");
            Contract.Requires(name != null, "name != null");

            return new PackageId(StringId.Create(stringTable, name), PackageVersion.None);
        }

        /// <summary>
        /// Creates a package id.
        /// </summary>
        public static PackageId Create(StringTable stringTable, string name, PackageVersion version)
        {
            Contract.Requires(stringTable != null);
            Contract.Requires(name != null);
            Contract.Requires(version != PackageVersion.None);

            return new PackageId(StringId.Create(stringTable, name), version);
        }

        /// <summary>
        /// Checks if this package id is valid.
        /// </summary>
        public bool IsValid => this != Invalid;

        /// <inheritdoc />
        public bool Equals(PackageId other)
        {
            return Name == other.Name && Version == other.Version;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Name.Value, Version.GetHashCode());
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
            if (stringTable == null)
            {
                return string.Empty;
            }

            if (!IsValid || !Name.IsValid)
            {
                return "Unknown";
            }

            return Name.ToString(stringTable) + (Version != PackageVersion.None ? "---" + Version.ToString(stringTable) : string.Empty);
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(PackageId left, PackageId right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(PackageId left, PackageId right)
        {
            return !left.Equals(right);
        }
    }
}
