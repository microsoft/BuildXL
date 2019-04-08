// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Model class for File details
    /// </summary>
    public sealed class FileDetails : FileReference, IComparable<FileDetails>, IEquatable<FileDetails>
    {
        /// <summary>
        /// File Size
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// True if the file is downloadable
        /// </summary>
        public bool IsDownloadable { get; set; }

        /// <summary>
        /// The pips that produce this file
        /// </summary>
        public IEnumerable<PipReference> ProducingPips { get; set; }

        /// <summary>
        /// Spec file that produced this file
        /// </summary>
        public FileReference SpecFile { get; set; }

        /// <summary>
        /// List of pips that consume this file
        /// </summary>
        public IEnumerable<PipReference> ConsumingPips { get; set; }

        /// <summary>
        /// Compares the file details' path to the other
        /// </summary>
        /// <param name="other">Other instance to compare to</param>
        /// <returns>
        /// Less than zero if this instance precedes other. Zero if this instance
        /// has the same position in the sort order as other. Greater than zero This instance
        /// follows other.
        /// </returns>
        public int CompareTo(FileDetails other)
        {
            return Compare(this, other, true);
        }

        /// <summary>
        /// Gets Hash code of obj
        /// </summary>
        /// <param name="obj">The file details to get the hash code for</param>
        /// <returns>hash code</returns>
        public static int GetHashCode(FileDetails obj)
        {
            Contract.Requires(obj != null);
            return obj.GetHashCode();
        }

        /// <inherit />
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        /// <summary>
        /// Checks whether two file details are the same.
        /// </summary>
        public static bool operator ==(FileDetails left, FileDetails right)
        {
            return Compare(left, right, false) == 0;
        }

        /// <summary>
        /// Checks whether two file details are different.
        /// </summary>
        public static bool operator !=(FileDetails left, FileDetails right)
        {
            return Compare(left, right, false) != 0;
        }

        /// <summary>
        /// Checks of the left is greater than the right
        /// </summary>
        public static bool operator >(FileDetails left, FileDetails right)
        {
            return Compare(left, right, true) > 0;
        }

        /// <summary>
        /// Checks of the left is greater than or equal to the right
        /// </summary>
        public static bool operator >=(FileDetails left, FileDetails right)
        {
            return Compare(left, right, true) >= 0;
        }

        /// <summary>
        /// Checks of the left is less than the right
        /// </summary>
        public static bool operator <(FileDetails left, FileDetails right)
        {
            return Compare(left, right, true) < 0;
        }

        /// <summary>
        /// Checks of the left is less than or equal to the right
        /// </summary>
        public static bool operator <=(FileDetails left, FileDetails right)
        {
            return Compare(left, right, true) <= 0;
        }

        /// <inherit />
        public bool Equals(FileDetails other)
        {
            return Compare(this, other, false) == 0;
        }

        /// <inherit />
        public override bool Equals(object obj)
        {
            return Compare(this, obj as FileDetails, false) == 0;
        }

        private static int Compare(FileDetails left, FileDetails right, bool fullCompare)
        {
            if (object.ReferenceEquals(left, right))
            {
                return 0;
            }

            if (object.ReferenceEquals(left, null))
                {
                return -1;
                }

            if (object.ReferenceEquals(right, null))
            {
                return 1;
            }

            if (left.Id == right.Id)
            {
                if (left.Id > 0)
                {
                    return 0;
                }
            }
            else if (!fullCompare)
            {
                return 1;   // comparing only for equality.
            }

            return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
