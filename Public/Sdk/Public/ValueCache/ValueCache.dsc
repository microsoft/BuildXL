// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/** 
 * Returns an object from the cache for a given key. 
 * Any type is allowed to be used as a key or as a value except functions.
 * 
 * Note: The object returned will always be a cloned copy.
 * DScript is a side effect free language, technically adding a pip to the build graph 
 * or printing a string to the console are side effects, but these  are not observable from the 
 * language itself. Yet we do use object identity for equality comparison so to avoid making cache 
 * hits observable to users we opt to clone the value from the cache each time, even after the
 * first time we add it to the cache.
 * This is also the reason why we don't have separate functions for tryGet and/or add
 * because the results would be observable and potentially invalidating all the
 * incremental evaluations in DScript.
 * 
 * Note: The motivation for not allowing functions is that they are hard to clone and hash.
 * It is technically feasable it was a tradeoff of implementation time. 
 * 
 * Note: This function might be removed in the futurue. A better way would be support duduplication
 * in the pipgrahp. There are some concerns there today with getNewOutputPath which currenlty would
 * make unique pips. If we extended that to allow relative paths as output files and then the pipgraph
 * can decide to create a unique output folder based ont the things it really depends on and be properly
 * deduplicated. THis way we will also automatically deduplicate code with qualifiers that don't have
 * any effect.
 */
@@public
export function getOrAdd<TKey, TValue>(key: TKey, createValue: () => TValue): TValue
{
    return _PreludeAmbientHack_ValueCache.getOrAdd(key, createValue);
}
