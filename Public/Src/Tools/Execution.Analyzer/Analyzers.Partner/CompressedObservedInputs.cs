// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer.Analyzers
{
    internal sealed class CompressedObservedInputs
    {
        private readonly Dictionary<int, ObservedInput> m_observedInputsCache = new Dictionary<int, ObservedInput>();
        private readonly Dictionary<ObservedInput, int> m_observedInputMap = new Dictionary<ObservedInput, int>();
        private int m_mObservedInputsIdIndex = 1;

        public ObservedInput GetObservedInputById(int id)
        {
            return m_observedInputsCache[id];
        }

        internal ReadOnlyArray<int> GetCompressedObservedInputs(ReadOnlyArray<ObservedInput> observedInputs)
        {
            var compressedObservedInputs = new int[observedInputs.Length];
            for (var index = 0; index < observedInputs.Length; index++)
            {
                var input = observedInputs[index];
                int value;
                if (!m_observedInputMap.TryGetValue(input, out value))
                {
                    value = m_mObservedInputsIdIndex++;
                    m_observedInputMap.Add(input, value);
                    m_observedInputsCache.Add(value, input);
                }

                compressedObservedInputs[index] = value;
            }

            return ReadOnlyArray<int>.From(compressedObservedInputs);
        }
    }
}
