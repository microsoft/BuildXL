// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using TypeScript.Net.Scanning;

namespace TypeScript.Net.Utilities
{
    /// <summary>
    /// Object pools used by this project.
    /// </summary>
    public static class Pools
    {
        /// <nodoc />
        public static readonly ObjectCache<CharArraySegment, string> StringCache = new ObjectCache<CharArraySegment, string>(18901);

        /// <nodoc/>
        public static readonly ObjectPool<TextBuilder> TextBuilderPool = new ObjectPool<TextBuilder>(() => new TextBuilder(), tb => { tb.Clear(); return tb; });

        /// <nodoc/>
        public static readonly ObjectPool<Scanner> ScannerPool = new ObjectPool<Scanner>(() => new Scanner(), s => s);

        /// <nodoc/>
        public static readonly ObjectPool<List<int>> LineMapPool = new ObjectPool<List<int>>(() => new List<int>(), tb => { tb.Clear(); return tb; });

        /// <summary>
        /// Clears all the pools and caches.
        /// </summary>
        public static void Clear()
        {
            TextBuilderPool.Clear();
            ScannerPool.Clear();
            LineMapPool.Clear();

            StringCache.Clear();
        }

    }
}
