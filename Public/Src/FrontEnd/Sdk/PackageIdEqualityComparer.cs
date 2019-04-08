// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// EqualityComparer for PackageId that allows control whether the version should be included in the comparison.
    /// </summary>
    public sealed class PackageIdEqualityComparer : IEqualityComparer<PackageId>
    {
        /// <summary>
        /// Name only comparer
        /// </summary>
        public static readonly PackageIdEqualityComparer NameOnly = new PackageIdEqualityComparer(false);

        /// <summary>
        /// Consider the name and version for comparison
        /// </summary>
        public static readonly PackageIdEqualityComparer NameAndVersion = new PackageIdEqualityComparer(true);

        /// <summary>
        /// Whether versions should be included in the comparison
        /// </summary>
        public readonly bool CompareVersions;

        /// <nodoc />
        private PackageIdEqualityComparer(bool compareVersion)
        {
            CompareVersions = compareVersion;
        }

        /// <inheritdoc />
        public bool Equals(PackageId x, PackageId y)
        {
            return x.Name == y.Name && (!CompareVersions || x.Version == y.Version);
        }

        /// <inheritdoc />
        public int GetHashCode(PackageId obj)
        {
            return CompareVersions ? HashCodeHelper.Combine(obj.Name.Value, obj.Version.GetHashCode()) : obj.Name.GetHashCode();
        }
    }
}
