// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>

// Mutable data structures can be used as an implementation details in performance sensitive parts of the applications (like SDK).
// BuildXL fails if mutable data structure is used for top-level variable or with a public/exposed functions.
// This is critical to allow incrementality and cacheability of the system.

/**
 * Unsafe mutable set.
 * 
 * 
 */
interface MutableSet<T> {
    /** Adds items to the set and returns current mutated instance. **/
    add: (...items: T[]) => MutableSet<T>;

    /** Checks if this set has the given item. */
    contains: (item: T) => boolean;

    /** Adds items from the given set and returns current mutated instance. */
    union: (set: MutableSet<T>) => MutableSet<T>;

    /** Removes items from the set and returns current mutated instance. */
    remove: (...items: T[]) => MutableSet<T>;

    /** Converts a set to an array */
    toArray: () => T[];

    /** Gets the size of this set. */
    count: () => number;
}

namespace MutableSet{
    export declare function empty<T>(): MutableSet<T>;
}
