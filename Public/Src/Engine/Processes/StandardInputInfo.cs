// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Info about the source of standard input.
    /// </summary>
    public class StandardInputInfo : IEquatable<StandardInputInfo>
    {
        /// <summary>
        /// File path.
        /// </summary>
        public string File { get; }

        /// <summary>
        /// Raw data.
        /// </summary>
        public string Data { get; }

        private StandardInputInfo(string file, string data)
        {
            Contract.Requires((file != null && data == null) || (file == null && data != null));

            File = file;
            Data = data;
        }

        /// <summary>
        /// Creates a standard input info where the source comes from a file.
        /// </summary>
        public static StandardInputInfo CreateForFile(string file)
        {
            Contract.Requires(!string.IsNullOrEmpty(file));

            return new StandardInputInfo(file, null);
        }

        /// <summary>
        /// Creates a standard input info where the source comes from raw data.
        /// </summary>
        public static StandardInputInfo CreateForData(string data)
        {
            Contract.Requires(data != null);

            return new StandardInputInfo(null, data);
        }

        /// <summary>
        /// Serializes this instance to a given <paramref name="writer"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.WriteNullableString(File);
            writer.WriteNullableString(Data);
        }

        /// <summary>
        /// Deserializes an instance of <see cref="StandardInputInfo"/>
        /// </summary>
        public static StandardInputInfo Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            return new StandardInputInfo(file: reader.ReadNullableString(), data: reader.ReadNullableString());
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return !(obj is null) && (ReferenceEquals(this, obj) || ((obj is StandardInputInfo info) && Equals(info)));
        }

        /// <summary>
        /// Checks for equality.
        /// </summary>
        public bool Equals(StandardInputInfo standardInputInfo)
        {
            return !(standardInputInfo is null)
                && (ReferenceEquals(this, standardInputInfo)
                    || (string.Equals(File, standardInputInfo.File, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Data, standardInputInfo.Data)));
        }

        /// <summary>
        /// Checks for equality.
        /// </summary>
        public static bool operator ==(StandardInputInfo info1, StandardInputInfo info2)
        {
            if (ReferenceEquals(info1, info2))
            {
                return true;
            }

            if (info1 is null)
            {
                return false;
            }

            return info1.Equals(info2);
        }

        /// <summary>
        /// Checks for disequality.
        /// </summary>
        public static bool operator !=(StandardInputInfo info1, StandardInputInfo info2) => !(info1 == info2);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(File != null ? File.GetHashCode() : -1, Data != null ? Data.GetHashCode() : -1);
        }
    }
}
