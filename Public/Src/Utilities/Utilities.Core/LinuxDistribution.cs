// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Core
{
    /// <nodoc />
    public class LinuxDistribution(string id, Version version)
    {
        /// <summary>
        /// Name of the Linux Distribution (eg: ubuntu)
        /// </summary>
        public string Id { get; } = id.ToLower();

        /// <summary>
        /// Version number (eg: 24.04)
        /// </summary>
        public Version Version { get; } = version;

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            var distribution = obj as LinuxDistribution;
            return distribution.Id == Id && distribution.Version.Major == Version.Major && distribution.Version.Minor == Version.Minor;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Id} {Version}";
        }
    }
}