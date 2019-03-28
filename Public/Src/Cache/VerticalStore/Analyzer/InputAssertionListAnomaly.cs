// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// A container class that holds the data needed to fully describe an input assertion list anomaly
    /// </summary>
    public sealed class InputAssertionListAnomaly
    {
        /// <summary>
        /// The strong fingerprint that has a small input assertion list file size
        /// </summary>
        private readonly StrongFingerprint m_sfpWithSmallInputAssertionList;

        /// <summary>
        /// The contents of the small input assertion list file
        /// </summary>
        private readonly string m_smallInputAssertionListContents;

        /// <summary>
        /// The strong fingerprint that has a large input assertion list file size
        /// </summary>
        private readonly StrongFingerprint m_sfpWithLargeInputAssertionList;

        /// <summary>
        /// The contents of the large input assertion list file
        /// </summary>
        private readonly string m_largeInputAssertionListContents;

        /// <summary>
        /// Creates an object that contains the data needed to fully describe an input assertion list anomaly
        /// </summary>
        /// <param name="sfpWithSmallInputAssertionList">The strong fingerprint of the smaller input assertion list</param>
        /// <param name="smallInputAssertionListContents">The contents of the smaller input assertion list</param>
        /// <param name="sfpWithLargeInputAssertionList">The strong fingerprint of the larger input assertion list</param>
        /// <param name="largeInputAssertionListContents">The contents of the larger input assertion list</param>
        public InputAssertionListAnomaly(StrongFingerprint sfpWithSmallInputAssertionList, string smallInputAssertionListContents, StrongFingerprint sfpWithLargeInputAssertionList, string largeInputAssertionListContents)
        {
            m_sfpWithSmallInputAssertionList = sfpWithSmallInputAssertionList;
            m_smallInputAssertionListContents = smallInputAssertionListContents;
            m_sfpWithLargeInputAssertionList = sfpWithLargeInputAssertionList;
            m_largeInputAssertionListContents = largeInputAssertionListContents;
        }

        /// <summary>
        /// Returns a string representation of the object that is suitable for logging.
        /// </summary>
        /// <returns>A string representation of the object that is suitable for logging</returns>
        public override string ToString()
        {
            return "StrongFingerprint " + m_sfpWithSmallInputAssertionList.ToString() + " has an input assertion list file size of " + m_smallInputAssertionListContents.Length +
                            " bytes which is significantly smaller than StrongFingerprint " + m_sfpWithLargeInputAssertionList.ToString() +
                            " which has an input assertion list file size of " + m_largeInputAssertionListContents.Length + " bytes." +
                            Environment.NewLine + "Deserialized contents of " + m_sfpWithSmallInputAssertionList.CasElement.ToString() + ": " + m_smallInputAssertionListContents +
                            Environment.NewLine + "Deserialized contents of " + m_sfpWithLargeInputAssertionList.CasElement.ToString() + ": " + m_largeInputAssertionListContents;
        }
    }
}
