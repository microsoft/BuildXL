// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Pips
{
    /// <summary>
    /// Representation of standard input, that can be either a file or data.
    /// </summary>
    public readonly struct StandardInput : IEquatable<StandardInput>
    {
        /// <summary>
        /// File as the standard input.
        /// </summary>
        public readonly FileArtifact File;

        /// <summary>
        /// Data as the standard input.
        /// </summary>
        public readonly PipData Data;

        /// <nodoc />
        private StandardInput(FileArtifact file, PipData data)
        {
            File = file;
            Data = data;
        }

        /// <summary>
        /// Invalid standard input.
        /// </summary>
        public static readonly StandardInput Invalid = new StandardInput(FileArtifact.Invalid, PipData.Invalid);

        /// <summary>
        /// Checks if this standard input is valid.
        /// </summary>
        public bool IsValid => this != Invalid;

        /// <summary>
        /// Checks if this standard input is <see cref="FileArtifact" />.
        /// </summary>
        public bool IsFile => File.IsValid;

        /// <summary>
        /// Checks if this standard input is <see cref="PipData" />.
        /// </summary>
        public bool IsData => Data.IsValid;

        /// <summary>
        /// Creates a standard input from <see cref="FileArtifact" />.
        /// </summary>
        public static StandardInput CreateFromFile(FileArtifact file)
        {
            Contract.Requires(file.IsValid);
            return new StandardInput(file, PipData.Invalid);
        }

        /// <summary>
        /// Creates a standard input from <see cref="PipData" />.
        /// </summary>
        public static StandardInput CreateFromData(in PipData data)
        {
            Contract.Requires(data.IsValid);
            return new StandardInput(FileArtifact.Invalid, data);
        }

        #region Conversions

        /// <summary>
        /// Converts from <see cref="FileArtifact" />.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator StandardInput(FileArtifact file)
        {
            return new StandardInput(file, PipData.Invalid);
        }

        /// <summary>
        /// Converts from <see cref="PipData" />.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator StandardInput(PipData data)
        {
            return new StandardInput(FileArtifact.Invalid, data);
        }
        #endregion

        #region IEquatable<StandardInput> implementation

        /// <inheritdoc />
        public bool Equals(StandardInput other)
        {
            return File == other.File && Data == other.Data;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(File.GetHashCode(), Data.GetHashCode());
        }

        /// <summary>
        /// Checks for equality.
        /// </summary>
        public static bool operator ==(StandardInput left, StandardInput right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Checks for inequality.
        /// </summary>
        public static bool operator !=(StandardInput left, StandardInput right)
        {
            return !(left == right);
        }
        #endregion
    }
}
