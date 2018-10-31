// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Represents a <c>\\?\Volume{GUID}\</c> style path to the root of a volume.
    /// </summary>
    /// <remarks>
    /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa365248(v=vs.85).aspx
    /// </remarks>
    public readonly struct VolumeGuidPath : IEquatable<VolumeGuidPath>
    {
        /// <summary>
        /// Invalid guid path (compares equal to all other invalid guid paths).
        /// </summary>
        public static readonly VolumeGuidPath Invalid = default(VolumeGuidPath);

        private readonly string m_path;

        private VolumeGuidPath(string path)
        {
            Contract.Requires(path != null);
            Contract.RequiresDebug(IsValidVolumeGuidPath(path));
            m_path = path;
        }

        /// <summary>
        /// Indicates if this instance is valid (note that <c>default(VolumeGuidPath)</c> is not).
        /// </summary>
        public bool IsValid => m_path != null;

        /// <summary>
        /// Returns the string representation of the volume GUID path (ends in a trailing slash).
        /// </summary>
        public string Path
        {
            get
            {
                Contract.Requires(IsValid);
                return m_path;
            }
        }

        /// <summary>
        /// Returns the string representation of the volume GUID device path (like <see cref="Path" /> but without a trailing
        /// slash).
        /// </summary>
        public string GetDevicePath()
        {
            Contract.Requires(IsValid);

            // Trim trailing backslash.
            // \\?\Volume{123}\ indicates the root directory on a volume whereas \\?\Volume{123} is the actual volume device.
            Contract.Assume(m_path[m_path.Length - 1] == '\\');
            return m_path.Substring(0, m_path.Length - 1);
        }

        /// <summary>
        /// Attempts to parse a string path as a volume guid path.
        /// </summary>
        public static bool TryCreate(string path, out VolumeGuidPath parsed)
        {
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out parsed).IsValid);

            if (!IsValidVolumeGuidPath(path))
            {
                parsed = Invalid;
                return false;
            }

            parsed = new VolumeGuidPath(path);
            return true;
        }

        /// <summary>
        /// Parses a string path as a volume guid path. The string path must be valid.
        /// </summary>
        public static VolumeGuidPath Create(string path)
        {
            bool parsed = TryCreate(path, out VolumeGuidPath result);
            Contract.Assume(parsed);
            return result;
        }

        /// <summary>
        /// Validates that the given string is a volume guid PATH.
        /// </summary>
        [Pure]
        public static bool IsValidVolumeGuidPath(string path)
        {
            Contract.Ensures(!Contract.Result<bool>() || !string.IsNullOrEmpty(path));
            const string VolumePrefix = @"\\?\Volume{";

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (!path.StartsWith(VolumePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // The last character should be a backslash (volume root directory), and it should be the only such slash after the prefix.
            if (path.IndexOf('\\', VolumePrefix.Length) != path.Length - 1)
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return m_path;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(VolumeGuidPath other)
        {
            return ReferenceEquals(m_path, other.m_path) ||
                   (m_path != null && m_path.Equals(other.m_path, StringComparison.OrdinalIgnoreCase));
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_path?.GetHashCode() ?? 0;
        }

        /// <nodoc />
        public static bool operator ==(VolumeGuidPath left, VolumeGuidPath right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(VolumeGuidPath left, VolumeGuidPath right)
        {
            return !left.Equals(right);
        }
    }
}
