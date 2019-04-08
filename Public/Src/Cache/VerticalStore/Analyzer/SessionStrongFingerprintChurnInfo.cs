// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// Provides summative data for the churn in the strong fingerprints of a session
    /// </summary>
    public class SessionStrongFingerprintChurnInfo
    {
        /// <summary>
        /// Total number of strong fingerprints referenced by the session
        /// </summary>
        public readonly int TotalNumberOfSessionStrongFingerprints;

        /// <summary>
        /// Number of unique strong fingerprints referenced by the session
        /// </summary>
        public readonly int NumberOfUniqueSessionStrongFingerprints;

        /// <summary>
        /// Number of unique weak fingerprints referenced by the session
        /// </summary>
        public readonly int NumberOfUniqueSessionWeakFingerprints;

        /// <summary>
        /// Percentage of the total number of strong fingerprints that are unique
        /// </summary>
        public readonly double PercentageUniqueStrongFingerprints;

        /// <summary>
        /// Consturcts an object that provides summative data for the churn in
        /// the strong fingerprints of a session
        /// </summary>
        /// <param name="totalNumberOfSessionStrongFingerprints">Total number
        /// of strong fingerprints referenced by the session</param>
        /// <param name="numberOfUniqueSessionStrongFingerprints">Number of
        /// unique strong fingerprints referenced by the session</param>
        /// <param name="numberOfUniqueSessionWeakFingerprints">Number of
        /// unique weak fingerprints referenced by the session</param>
        public SessionStrongFingerprintChurnInfo(
            int totalNumberOfSessionStrongFingerprints,
            int numberOfUniqueSessionStrongFingerprints,
            int numberOfUniqueSessionWeakFingerprints)
        {
            TotalNumberOfSessionStrongFingerprints = totalNumberOfSessionStrongFingerprints;
            NumberOfUniqueSessionStrongFingerprints = numberOfUniqueSessionStrongFingerprints;
            PercentageUniqueStrongFingerprints = ((double)numberOfUniqueSessionStrongFingerprints / totalNumberOfSessionStrongFingerprints) * 100;

            NumberOfUniqueSessionWeakFingerprints = numberOfUniqueSessionWeakFingerprints;
        }

        private const string CSVFormat = "{0}, {1}, {2}, {3}";

        /// <summary>
        /// Returns a CSV appropriate header for the data contained in this
        /// class
        /// </summary>
        /// <returns>A CSV appropriate header for the data contained in this
        /// class</returns>
        public static string GetHeader()
        {
            string returnValue = string.Format(CultureInfo.InvariantCulture, CSVFormat, "Total Strong Fingerprint (SFP)", "# Unique SFP Over Time", "% Unique SFP Over Time", "# Unique WFP Over Time");
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
            string returnValue = string.Format(CultureInfo.InvariantCulture, CSVFormat, TotalNumberOfSessionStrongFingerprints, NumberOfUniqueSessionStrongFingerprints, PercentageUniqueStrongFingerprints, NumberOfUniqueSessionWeakFingerprints);
            return returnValue;
        }
    }
}
