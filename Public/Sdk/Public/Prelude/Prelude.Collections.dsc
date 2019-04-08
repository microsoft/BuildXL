// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>

/**
 * Interface for Map.
 * Key comparison is based on hash code and equality comparer. Primitives, like number, string,
 * enum, path, file, and directory, have their internal implementations for hash code and equality
 * comparer. Object literals, including arrays, use pointer comparison.
 * In the future, we may want to allow users to specify their own comparer.
 */
interface Map<K, V> {
    __mapKeyBrand: K;
    __mapValueBrand: V;

    /** Adds a key-value pair, overwritting exisiting pair that has the same key, if any. **/
    add: (key: K, value: V) => Map<K, V>;

    /** Adds a sequence of key-value pairs. **/
    addRange: (...kvps: [K, V][]) => Map<K, V>;

    /** Checks if this map has the given key. */
    containsKey: (key: K) => boolean;

    /** Gets the value associated with the given key; returns undefined if key is not contained in the map. */
    get: (key: K) => V;

    /** Removes the mapping that has the given key. */
    remove: (key: K) => Map<K, V>;

    /** Removes the mappings that have the given list of keys. */
    removeRange: (...keys: K[]) => Map<K, V>;

    /** Gets the key-value pair array of this map. */
    toArray: () => [K, V][];

    /** For each iterator. */
    forEach: <T> (func: (kvp: [K, V]) => T) => T[];

    /** Gets the available keys in this map. */
    keys: () => K[];

    /** Gets the available values in this map. */
    values: () => V[];

    /** Gets the size of this map. */
    count: () => number;
}

namespace Map {
    export declare function empty<K, V>(): Map<K, V>;
    export declare function emptyCaseInsensitive<V>(): Map<string, V>;
}

/**
 * Interface for Set.
 * This set is backed by a hash set. Thus, item comparison is based on hash code and equality comparer.
 * Primitives, like number, string, enum, path, file, and directory, have their internal implementations
 * for hash code and equality comparer. Object literals, including arrays, use pointer comparison.
 */
interface Set<T> {
    __setItemBrand: T;

    /** Creates a new set which is a union of the current set and the given items. **/
    add: (...items: T[]) => Set<T>;

    /** Checks if this set has the given item. */
    contains: (item: T) => boolean;

    /** Creates a new set which is a difference of the current set and the given items. */
    remove: (...items: T[]) => Set<T>;

    /** Unions set. */
    union: (set: Set<T>) => Set<T>;

    /** Intersects set. */
    intersect: (set: Set<T>) => Set<T>;

    /** Except set. */
    except: (set: Set<T>) => Set<T>;

    /** Checks for subset relation. */
    isSubsetOf: (set: Set<T>) => boolean;

    /** Check for proper subset relation. */
    isProperSubsetOf: (set: Set<T>) => boolean;

    /** Checks for superset relation. */
    isSupersetOf: (set: Set<T>) => boolean;

    /** Check for proper superset relation. */
    isProperSupersetOf: (set: Set<T>) => boolean;

    /** Gets the item array of this set. */
    toArray: () => T[];

    /** For each iterator. */
    forEach: <S> (func: (T) => S) => S[];

    /** Gets the size of this set. */
    count: () => number;
}

namespace Set {
    /** Creates an empty Set */
    export declare function empty<T>(): Set<T>;

    /** Creates a Set initialized with items. Calling with no parameters will result in an empty Set */
    export declare function create<T>(...items: T[]) : Set<T>;
}
