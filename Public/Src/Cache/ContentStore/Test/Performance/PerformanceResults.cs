// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using ContentStoreTest.Test;

namespace ContentStoreTest.Performance
{
    public class PerformanceResults
    {
        private readonly Dictionary<string, Tuple<long, string, long>> _results =
            new Dictionary<string, Tuple<long, string, long>>(StringComparer.OrdinalIgnoreCase);

        public void Add(string name, long value, string units, long count = 0)
        {
            Contract.Requires(name != null);
            _results.Add(name, Tuple.Create(value, units, count));
        }

        public void Report()
        {
            if (_results.Count > 0)
            {
                var logger = TestGlobal.Logger;

                foreach (var kvp in _results)
                {
                    var itemCount = kvp.Value.Item3;
                    if (itemCount > 0)
                    {
                        logger.Always("{0} = {1} {2} ({3} items)", kvp.Key, kvp.Value.Item1, kvp.Value.Item2, kvp.Value.Item3);
                    }
                    else
                    {
                        logger.Always("{0} = {1} {2}", kvp.Key, kvp.Value.Item1, kvp.Value.Item2);
                    }
                }
            }
        }
    }
}
