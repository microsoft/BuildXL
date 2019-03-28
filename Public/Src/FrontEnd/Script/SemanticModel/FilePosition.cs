// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Utilities;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Absolute position within a file represented by an <see cref="AbsolutePath"/>.
    /// </summary>
    public readonly struct FilePosition : IEquatable<FilePosition>
    {
        /// <summary>
        /// Absolute position within a file.
        /// </summary>
        public int Position { get; }

        /// <summary>
        /// Absolute file name of a symbol.
        /// </summary>
        public AbsolutePath Path { get; }

        /// <summary>
        /// Returns true if the location is valid.
        /// </summary>
        public bool IsValid => Path.IsValid;

        /// <nodoc/>
        public FilePosition(INode node, AbsolutePath path)
            : this(CreatePosition(node), path)
        {
            Contract.Requires(node != null);

            Position = CreatePosition(node);
            Path = path;
        }

        /// <nodoc/>
        public FilePosition(int position, AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            Position = position;
            Path = path;
        }

        /// <summary>
        /// Gets position for a given node.
        /// </summary>
        /// <remarks>
        /// -1 is a special file position that means 'the entire file'.
        /// In case of an export {x}; expression, if the 'x' comes from another file
        /// the checker resolves the 'x' as a whole file.
        /// To represent this case, special position is used.
        /// </remarks>
        public static int CreatePosition(INode node)
        {
            Contract.Requires(node != null);
            return node.Kind == TypeScript.Net.Types.SyntaxKind.SourceFile ? -1 : node.Pos;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (!IsValid)
            {
                return "Invalid";
            }

            // Not printing a file name, because it will require context.
            return I($"Position: {Position.ToString(CultureInfo.InvariantCulture)}");
        }

        /// <inheritdoc/>
        public bool Equals(FilePosition other)
        {
            return Position == other.Position && Path.Equals(other.Path);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is FilePosition && Equals((FilePosition)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Position, Path.GetHashCode());
        }

        /// <nodoc/>
        public static bool operator ==(FilePosition left, FilePosition right)
        {
            return left.Equals(right);
        }

        /// <nodoc/>
        public static bool operator !=(FilePosition left, FilePosition right)
        {
            return !left.Equals(right);
        }
    }
}
