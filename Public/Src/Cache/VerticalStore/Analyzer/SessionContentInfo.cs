// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// Describes how much content a session referenced and introduced
    /// </summary>
    public class SessionContentInfo
    {
        /// <summary>
        /// Total number of files in the CAS referenced by this session
        /// </summary>
        public readonly long TotalContentCount;

        /// <summary>
        /// Sum of the lengths of files in the CAS referenced by this session
        /// </summary>
        public readonly long TotalContentSize;

        /// <summary>
        /// Total number of files in the CAS added by this session
        /// </summary>
        public readonly long NewContentCount;

        /// <summary>
        /// Sum of the lengths of files in the CAS added by this session
        /// </summary>
        public readonly long NewContentSize;

        /// <summary>
        /// Number of errors encountered when determing content info
        /// </summary>
        public readonly int ContentErrors;

        /// <summary>
        /// SessionContentInfo object where all data is set to zero
        /// </summary>
        public static readonly SessionContentInfo None = new SessionContentInfo(0, 0, 0, 0, 0);

        /// <summary>
        /// Constructs an object which describes how much content a session
        /// referenced and introduced
        /// </summary>
        /// <param name="totalContentCount">Total number of files in the CAS
        /// referenced by this session</param>
        /// <param name="totalContentSize">Sum of the lengths of files in the
        /// CAS referenced by this session</param>
        /// <param name="newContentCount">Total number of files in the CAS
        /// added by this session</param>
        /// <param name="newContentSize">Sum of the lengths of files in the CAS
        /// added by this session</param>
        /// <param name="contentErrors">Number of errors encountered when
        /// determing content info</param>
        public SessionContentInfo(
            long totalContentCount,
            long totalContentSize,
            long newContentCount,
            long newContentSize,
            int contentErrors)
        {
            NewContentCount = newContentCount;
            NewContentSize = newContentSize;
            TotalContentCount = totalContentCount;
            TotalContentSize = totalContentSize;
            ContentErrors = contentErrors;
        }

        private const string CSVFormat = "{0}, {1}, {2}, {3}, {4}";

        /// <summary>
        /// Returns a CSV appropriate header for the data contained in this
        /// class
        /// </summary>
        /// <returns>A CSV appropriate header for the data contained in this
        /// class</returns>
        public static string GetHeader()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                CSVFormat,
                "Total Content Count",
                "Total Content Size",
                "New Content Count",
                "New Content Size",
                "Content Errors");
        }

        /// <summary>
        /// Returns the data contained in this class in a string formatted for
        /// a CSV file
        /// </summary>
        /// <returns>The data contained in this class in a string formatted for
        /// a CSV file</returns>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                CSVFormat,
                TotalContentCount,
                TotalContentSize,
                NewContentCount,
                NewContentSize,
                ContentErrors);
        }
    }
}
