// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Scanning;

namespace TypeScript.Net.Utilities
{
    /// <summary>
    /// Lazily computed line information.
    /// </summary>
    public struct LineInfo : IEquatable<LineInfo>
    {
        private volatile LineMap m_map;

        // To save space m_position is used to specify both: absolute position and position within a column
        private int m_line;
        private int m_position;

        /// <nodoc />
        private LineInfo([NotNull]LineMap map, int position)
            : this()
        {
            Contract.Requires(map != null);
            m_map = map;
            m_position = position;
        }

        private LineInfo(int line, int position)
            : this()
        {
            m_line = line;
            m_position = position;
        }

        /// <nodoc />
        public static LineInfo FromLineMap([NotNull] LineMap map, int position)
        {
            return new LineInfo(map, position);
        }

        /// <nodoc />
        public static LineInfo FromLineAndPosition(int line, int position)
        {
            return new LineInfo(line, position);
        }

        /// <nodoc/>
        public int Line
        {
            get
            {
                ComputeLineAndColumnIfNeeded();
                return m_line;
            }
        }

        /// <nodoc />
        public int Position
        {
            get
            {
                ComputeLineAndColumnIfNeeded();
                return m_position;
            }
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Line, Position);
        }

        /// <inheritdoc />
        public bool Equals(LineInfo other)
        {
            if (m_map != null && other.m_map != null)
            {
                return m_position == other.m_position;
            }

            return other.Position == Position && other.Line == Line;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(LineInfo left, LineInfo right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(LineInfo left, LineInfo right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Writes this line info without forcing the line and column computation (if that hasn't happened yet)
        /// </summary>
        public void Write(BuildXLWriter writer, bool forceLineAndColumn = false)
        {
            if (forceLineAndColumn)
            {
                ComputeLineAndColumnIfNeeded();
            }

            // If the line info is not expanded we store the absolute value, so
            // we don't force the expansion
            if (m_map != null)
            {
                writer.Write(true); // isUnexpanded?
                writer.WriteCompact(m_position);
            }
            else
            {
                writer.Write(false);
                writer.WriteCompact(Line);
                writer.WriteCompact(Position);
            }
        }

        /// <summary>
        /// Reads a line info using the provided reader
        /// </summary>
        /// <remarks>
        /// If the line info was originally stored in unexpanded form, then the provided line map is used
        /// to construct the instance.
        /// </remarks>
        public static LineInfo Read(LineMap lineMap, BuildXLReader reader)
        {
            var isUnexpanded = reader.ReadBoolean();
            if (isUnexpanded)
            {
                var absolutePosition = reader.ReadInt32Compact();
                return FromLineMap(lineMap, absolutePosition);
            }

            int line = reader.ReadInt32Compact();
            int position = reader.ReadInt32Compact();
            return FromLineAndPosition(line, position);
        }

        private void ComputeLineAndColumnIfNeeded()
        {
            if (m_map != null)
            {
                var lineAndColumn = Scanner.ExpensiveComputeLineAndCharacterOfPositionSeeTask646652(m_map.Map, m_position);

                m_position = lineAndColumn.Character;
                m_line = lineAndColumn.Line;

                m_map = null;
            }
        }
    }
}
