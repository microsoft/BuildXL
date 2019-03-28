// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Results
{
    /// <summary>
    /// A list of selectors obtained by calling <see cref="IReadOnlyMemoizationSessionWithLevelSelectors.GetLevelSelectorsAsync"/> call.
    /// </summary>
    public class LevelSelectors
    {
        /// <nodoc />
        public IReadOnlyList<Selector> Selectors { get; }

        /// <summary>
        /// True if the selectors from a next level are available.
        /// </summary>
        public bool HasMore { get; }

        /// <inheritdoc />
        public LevelSelectors(IReadOnlyList<Selector> selectors, bool hasMore)
        {
            Selectors = selectors;
            HasMore = hasMore;
        }

        /// <nodoc />
        public static Result<LevelSelectors> Single(Result<Selector[]> selectors)
        {
            if (selectors)
            {
                return new LevelSelectors(selectors.Value, hasMore: false);
            }

            return new Result<LevelSelectors>(selectors);
        }
    }
}
