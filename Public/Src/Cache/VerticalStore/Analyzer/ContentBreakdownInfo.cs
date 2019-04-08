// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// This class is used to represent content statistics for a session.
    /// </summary>
    public sealed class ContentBreakdownInfo
    {
        /// <summary>
        /// Specifies the name of the session for the break down info.
        /// </summary>
        public string SessionName { get; private set; }

        /// <summary>
        /// Specifies the sizes of input lists in this session.
        /// </summary>
        public ContentBreakdownTable CasElementSizes { get; private set; }

        /// <summary>
        /// Specifies the sizes of content in this session.
        /// </summary>
        public ContentBreakdownTable CasEntrySizes { get; private set; }

        /// <summary>
        /// Initializes a new instance of the ContentBreakdownInfo class.
        /// </summary>
        public ContentBreakdownInfo(string sessionName, Dictionary<CasHash, long> casElements, Dictionary<CasHash, long> casEntries)
        {
            SessionName = sessionName;
            CasElementSizes = new ContentBreakdownTable(casElements);
            CasEntrySizes = new ContentBreakdownTable(casEntries);
        }
    }
}
