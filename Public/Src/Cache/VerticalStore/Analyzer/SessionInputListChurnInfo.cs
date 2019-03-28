// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// Provides summative data for the churn in the input lists of a session
    /// </summary>
    public class SessionInputListChurnInfo
    {
        /// <summary>
        /// Total number of input lists referenced by the session
        /// </summary>
        public readonly int TotalNumberOfInputLists;

        /// <summary>
        /// Number of unique input lists referenced by the session
        /// </summary>
        public readonly int NumberOfUniqueInputLists;

        /// <summary>
        /// Number of unique input lists introduced to cache by the session
        /// </summary>
        public readonly int NumberOfUniqueInputListsIntroducedToCache;

        /// <summary>
        /// Percentage of the total number of input lists that are unique
        /// </summary>
        public readonly double PercentageUniqueInputListsWithinSession;

        /// <summary>
        /// Percentage of the total number of input lists that were introduced
        /// to cache
        /// </summary>
        public readonly double PercentageUniqueInputListsIntroducedToCache;

        /// <summary>
        /// Number of empty input lists for the session
        /// </summary>
        public readonly int NumberOfEmptyInputLists;

        /// <summary>
        /// Constructs an object that provides summative data for the churn in
        /// the input lists of a session
        /// </summary>
        /// <param name="totalNumberOfInputLists">Total number of input lists
        /// referenced by the session</param>
        /// <param name="numberOfUniqueInputLists">Number of unique input lists
        /// referenced by the session</param>
        /// <param name="numberOfUniqueInputListsIntroducedToCache">Number of
        /// unique input lists introduced to cache by the session</param>
        /// <param name="numberOfEmptyInputLists">Number of empty input lists
        /// for the session</param>
        public SessionInputListChurnInfo(
            int totalNumberOfInputLists,
            int numberOfUniqueInputLists,
            int numberOfUniqueInputListsIntroducedToCache,
            int numberOfEmptyInputLists)
        {
            TotalNumberOfInputLists = totalNumberOfInputLists;
            NumberOfUniqueInputLists = numberOfUniqueInputLists;
            NumberOfUniqueInputListsIntroducedToCache = numberOfUniqueInputListsIntroducedToCache;
            PercentageUniqueInputListsWithinSession = ((double)NumberOfUniqueInputLists / TotalNumberOfInputLists) * 100;
            PercentageUniqueInputListsIntroducedToCache = ((double)NumberOfUniqueInputListsIntroducedToCache / TotalNumberOfInputLists) * 100;
            NumberOfEmptyInputLists = numberOfEmptyInputLists;
        }

        private const string CSVFormat = "{0}, {1}, {2}, {3}, {4}, {5}";

        /// <summary>
        /// Returns a CSV appropriate header for the data contained in this
        /// class
        /// </summary>
        /// <returns>A CSV appropriate header for the data contained in this
        /// class</returns>
        public static string GetHeader()
        {
            string returnValue = string.Format(CultureInfo.InvariantCulture, CSVFormat,
                            "Total Input List (IL)",
                            "# Unique IL Over Time",
                            "% Unique IL Over Time",
                            "# Unique IL For Session",
                            "% Unique IL For Session",
                            "# No Item IL");

            return returnValue;
        }

        /// <summary>
        /// Returns the data contained in this class in a string formatted for
        /// a CSV file
        /// </summary>
        /// <returns>The data contained in this class in a string formatted for
        /// a CSV file</returns>
        public override string ToString()
        {
            string returnValue = string.Format(CultureInfo.InvariantCulture, CSVFormat,
                TotalNumberOfInputLists,
                NumberOfUniqueInputListsIntroducedToCache,
                PercentageUniqueInputListsIntroducedToCache,
                NumberOfUniqueInputLists,
                PercentageUniqueInputListsWithinSession,
                NumberOfEmptyInputLists);

            return returnValue;
        }
    }
}
