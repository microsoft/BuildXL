// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Similar to <see cref="LocationData"/> but with lazy evaluation semantic.
    /// </summary>
    public readonly struct LazyLocationData : IEquatable<LazyLocationData>
    {
        /// <nodoc />
        public static LazyLocationData Invalid { get; } = default(LazyLocationData);

        /// <nodoc />
        public LazyLocationData(LineInfo lazyLineInfo, AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            LazyLineInfo = lazyLineInfo;
            Path = path;
        }

        /// <nodoc />
        public bool IsValid => Path.IsValid;

        /// <summary>
        /// Lazy line and column.
        /// </summary>
        public LineInfo LazyLineInfo { get; }

        /// <summary>
        /// Absolute path.
        /// </summary>
        public AbsolutePath Path { get; }

        /// <summary>
        /// Converts a LocationData object to a Log Location
        /// </summary>
        public Location ToLogLocation(PathTable pathTable)
        {
            return new Location()
                   {
                       File = Path.ToString(pathTable),
                       Line = LazyLineInfo.Line,
                       Position = LazyLineInfo.Position,
                   };
        }

        /// <inheritdoc />
        public bool Equals(LazyLocationData other)
        {
            return LazyLineInfo.Equals(other.LazyLineInfo) && Path.Equals(other.Path);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is LazyLocationData && Equals((LazyLocationData)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(LazyLineInfo.GetHashCode(), Path.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(LazyLocationData left, LazyLocationData right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(LazyLocationData left, LazyLocationData right)
        {
            return !left.Equals(right);
        }
    }
}
