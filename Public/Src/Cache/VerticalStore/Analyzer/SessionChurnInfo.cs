// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// Describes how much new stuff a session introduced into the cache
    /// </summary>
    public class SessionChurnInfo
    {
        /// <summary>
        /// Name of the session
        /// </summary>
        public readonly string SessionName;

        /// <summary>
        /// Describes how many new strong fingerprints this session introduced
        /// </summary>
        public readonly SessionStrongFingerprintChurnInfo StrongFingerprintChurnInfo;

        /// <summary>
        /// Describes how many new input lists this session introduced
        /// </summary>
        public readonly SessionInputListChurnInfo InputListChurnInfo;

        /// <summary>
        /// Describes how much content a session referenced and introduced
        /// </summary>
        public readonly SessionContentInfo ContentInfo;

        /// <summary>
        /// Constructs a SessionChurnInfo object which describes how much new
        /// stuff the session introduced into the cache
        /// </summary>
        /// <param name="sessionName">Name of the session</param>
        /// <param name="strongFingerprintChurnInfo">Describes how many new
        /// strong fingerprints this session introduced</param>
        /// <param name="inputListChurnInfo">Describes how many new input lists
        /// this session introduced</param>
        /// <param name="contentInfo">Describes how much content a session
        /// referenced and introduced</param>
        public SessionChurnInfo(
            string sessionName,
            SessionStrongFingerprintChurnInfo strongFingerprintChurnInfo,
            SessionInputListChurnInfo inputListChurnInfo,
            SessionContentInfo contentInfo)
        {
            SessionName = sessionName;
            StrongFingerprintChurnInfo = strongFingerprintChurnInfo;
            InputListChurnInfo = inputListChurnInfo;
            ContentInfo = contentInfo ?? SessionContentInfo.None;
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
            return string.Format(
                CultureInfo.InvariantCulture,
                CSVFormat,
                "Session ID",
                SessionStrongFingerprintChurnInfo.GetHeader(),
                SessionInputListChurnInfo.GetHeader(),
                SessionContentInfo.GetHeader());
        }

        /// <summary>
        /// Returns the data contained in this class in a string formatted for
        /// a CSV file
        /// </summary>
        /// <returns>The data contained in this class in a string formatted for
        /// a CSV file</returns>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, CSVFormat, SessionName, StrongFingerprintChurnInfo, InputListChurnInfo, ContentInfo);
        }
    }
}
