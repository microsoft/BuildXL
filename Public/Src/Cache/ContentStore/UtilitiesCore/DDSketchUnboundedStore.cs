// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

#nullable enable

namespace BuildXL.Cache.ContentStore.UtilitiesCore.Sketching
{
    /// <summary>
    /// An store that will grow unbounded in terms of memory usage. This has the benefit of getting as precise as
    /// required, but may use a bunch of memory. For relatively large sketch alpha (i.e. 0.01), this uses very little
    /// memory. However, values like 1e-5 make it explode.
    /// </summary>
    public sealed class DDSketchUnboundedStore : DDSketchStore
    {
        // TODO: sorted dict?
        private Dictionary<int, int> _data = new Dictionary<int, int>();

        /// <inheritdoc />
        public override void Add(int index)
        {
            if (!_data.ContainsKey(index))
            {
                _data[index] = 1;
            }
            else
            {
                _data[index] += 1;
            }
        }

        /// <inheritdoc />
        public override int IndexOf(int rank)
        {
            int seen = 0;
            foreach (var idx in _data.Keys.OrderBy(x => x))
            {
                seen += _data[idx];

                if (seen >= rank)
                {
                    return idx;
                }
            }

            return 0;
        }

        /// <inheritdoc />
        public override void Copy(DDSketchStore store_)
        {
            Contract.Requires(store_ is DDSketchUnboundedStore);
            var store = (DDSketchUnboundedStore)store_;
            _data = new Dictionary<int, int>(store._data);
        }

        /// <inheritdoc />
        public override void Merge(DDSketchStore store_)
        {
            Contract.Requires(store_ is DDSketchUnboundedStore);
            var store = (DDSketchUnboundedStore)store_;
            foreach (var kvp in store._data)
            {
                if (!_data.ContainsKey(kvp.Key))
                {
                    _data[kvp.Key] = kvp.Value;
                }
                else
                {
                    _data[kvp.Key] += kvp.Value;
                }
            }
        }
    }
}
