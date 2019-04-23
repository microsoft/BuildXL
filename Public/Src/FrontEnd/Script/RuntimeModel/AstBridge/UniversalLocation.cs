// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.Types;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Represents a location that could be converted to different representations, like <see cref="Location"/> for logging, <see cref="FilePosition"/> for name resolution etc.
    /// </summary>
    /// <remarks>
    /// This struct is used only during AST conversion and helps to abstract away different representations that are needed by different implementation and parts of the conversion.
    /// </remarks>
    public readonly struct UniversalLocation : IEquatable<UniversalLocation>
    {
        private readonly AbsolutePath m_absolutePath;
        private readonly PathTable m_pathTable;
        private readonly int m_absolutePosition;
        private readonly LineInfo m_lineInfo;

        /// <nodoc />
        public UniversalLocation(INode node, LineInfo lineInfo, AbsolutePath absolutePath, PathTable pathTable)
        {
            m_absolutePosition = node != null ? FilePosition.CreatePosition(node) : -1;
            m_lineInfo = lineInfo;
            m_absolutePath = absolutePath;
            m_pathTable = pathTable;
        }

        internal UniversalLocation(DeserializationContext context, LineInfo location)
        {
            m_absolutePath = context.Reader.ReadAbsolutePath();
            m_absolutePosition = context.Reader.ReadInt32();
            m_pathTable = context.PathTable;
            m_lineInfo = LineInfo.Read(context.LineMap, context.Reader);
        }

        /// <nodoc />
        public static UniversalLocation FromLineInfo(LineInfo lineInfo, AbsolutePath absolutePath, PathTable pathTable)
        {
            return new UniversalLocation(null, lineInfo, absolutePath, pathTable);
        }

        /// <summary>
        /// Serializes into the given writer.
        /// </summary>
        public void DoSerialize(BuildXLWriter writer, bool forceLineAndColumn = false)
        {
            writer.Write(m_absolutePath);
            writer.Write(m_absolutePosition);

            m_lineInfo.Write(writer, forceLineAndColumn);
        }

        /// <nodoc />
        public AbsolutePath File => m_absolutePath;

        /// <summary>
        /// Returns location for logging.
        /// </summary>
        /// <returns>
        /// This method materialize a location!
        /// </returns>
        public Location AsLoggingLocation() { return m_lineInfo.AsLoggingLocation(m_absolutePath.ToString(m_pathTable)); }

        /// <nodoc />
        public FilePosition AsFilePosition() { return new FilePosition(m_absolutePosition, m_absolutePath); }

        /// <nodoc />
        public LineInfo AsLineInfo() { return m_lineInfo; }

        /// <nodoc />
        public static implicit operator LineInfo(UniversalLocation location)
        {
            return location.AsLineInfo();
        }

        /// <inheritdoc/>
        public bool Equals(UniversalLocation other)
        {
            return m_absolutePath.Equals(other.m_absolutePath) && m_absolutePosition == other.m_absolutePosition && m_lineInfo.Equals(other.m_lineInfo);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is UniversalLocation && Equals((UniversalLocation)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(m_absolutePath.GetHashCode(), m_lineInfo.GetHashCode(), m_absolutePosition.GetHashCode());
        }

        /// <nodoc/>
        public static bool operator ==(UniversalLocation left, UniversalLocation right)
        {
            return left.Equals(right);
        }

        /// <nodoc/>
        public static bool operator !=(UniversalLocation left, UniversalLocation right)
        {
            return !left.Equals(right);
        }
    }
}
