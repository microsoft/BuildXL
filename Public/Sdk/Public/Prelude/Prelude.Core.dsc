// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference no-default-lib="true"/>

/**
 * Function that could be used as an ambient annotation to mark that some type or member should not be used any more.
 *
 * Example:
 * @@obsolete()
 * function doNotUseAnyMore() { return 42;}
 * const x = doNotUseAnyMore(); // warning 'doNotUseAnyMore' is obsolete
 */
declare function obsolete(msg?: string): (string) => string;

//-----------------------------------------------------------------------------
//  Object
//-----------------------------------------------------------------------------

/** All interfaces implements Object */
interface Object {
    /** Returns a string representation of an object. */
    // TODO: currently interpreter does not support toString methods for all objects!
    // To avoid potential misuse of the Path.toString we need to rename this method to debugToString or similar
    toString(): string;

    /**
     * Creates a new instance based on current one with additional members from 'other' object.
     *
     * Current method is intentially not strongly typed. Here is a reason for this:
     * In some cases 'T' could have several required properties but user wants to override
     * just one.
     * If 'other' is of type 'T' user have to specified all required properties manually
     * with old values otherwise this method will override them with potentially invalid values.
     *
     * Here is an example:
     * interface Foo {x: number, y: number, z?: number};
     * let f: Foo = {x: 1, y: 2, z: 3};
     * let f2 = f.override<Foo>({x: 42});
     *
     * if 'other' argument would be of type T user have to write following:
     * let f3 = f.override<Foo>({x: 42, y: f.x});
     *
     * Current design could lead to potential errors, but Interpreter can easily catch them.
     */
    override<T extends Object>(other: (T| Object)): T;

    /**
     * Creates a new instance where the value of a given key is replaced by a given value.
     *
     * This function is similar to 'override', except it allows for the key to be specified
     * as a value, as opposed to a literal.
     *
     * Examples:
     *
     *                      {}.overrideKey("x", 42) ===  {x: 42}
     *                  {x: 0}.overrideKey("x", 42) ===  {x: 42}
     *   let key = "x"; {x: 0}.overrideKey(key, 42) ===  {x: 42}
     */
    overrideKey<T extends Object>(key: string, value: (T | Object)): T;

    /**
     * Implements the semantics of BuildXL templates. This function is for now WDG-specific and the behavior is likely to change.
     * Please use 'override' if you don't need any specific prepend/append/replace behavior for arrays
     *
     * Here is an example:
     *
     *   interface Foo {x: number, y: number[]};
     *   let f: Foo = {x: 1, y: [2, 3]};
     *   let f2 = f.override<Foo>({x: 42, y: [4]});
     *
     * The result is
     *
     *   f2 == {x: 42, y: [2, 3, 4]}
     */
    merge<T extends Object>(other: (T | Object)): T;

    /**
     * Returns the a collection of all the keys that are part of the object
     */
    keys(): string[];

    /**
     * Returns the value associated with a key of the object. Undefined is returned in case the key does not exist.
     */
    get(key: string): Object;

    /** The function to call when merging this object. If undefined, a default merge is applied */
    customMerge?: CustomMergeFunction<Object>;

    /** Returns a new object that is exactly like this object but with the given custom merge function set.*/
    withCustomMerge<T extends Object>(customMergeFunction: CustomMergeFunction<T>) : T;
}

/** An intrinsic object that serves as holder of global object functions */
namespace Object {
    /** Merges objects */
    export declare function merge<T extends Object>(...items: (T | Object)[]): T;
}

/**
 * A custom merge function defines how two objects get merged
 */
type CustomMergeFunction<T> = (left: T, right : T) => T;

//-----------------------------------------------------------------------------
//  String
//-----------------------------------------------------------------------------

/**
 * 2-bytes character string encoded via USC-2 enconding.
 */
interface String {

    //Built-in
    //function (===)( string, string ) : boolean;
    //function (!==)( string, string ) : boolean;
    //function (+)( string, string ) : string;
    //function (<)( x : string, y : string ) : boolean;
    //function (<=)( x : string, y : string ) : boolean;
    //function (>)( x : string, y : string ) : boolean;
    //function (>=)( x : string, y : string ) : boolean;

    /**
     * Returns the character at the specified index.
     *
     * If the index is out of bounds, an error is returned
     * (in TypeScript the result would be an empty string, but DScript is stricter).
     */
    charAt(index: number): string;

    /**
     * Returns the Unicode value of the character at the specified index.
     *
     * If the index is out of bounds, an error is returned
     * (in TypeScript the result would be NaN, but DScript is stricter ).
     */
    charCodeAt(index: number): number;

    /** Returns a string that contains the concatenation of two or more strings.*/
    concat(...strings: string[]): string;

    /** Does this string contain the other string ? */
    contains(other: string): boolean;

    /** Does this string end with suffix? */
    endsWith(suffix: string): boolean;

    /** Returns the zero-based index of the first occurrence of the search string after given position, default 0. */
    indexOf(searchString: string, position?: number): number;

    /** Returns the zero-based index of the last occurrence of the search string strictly before position, default this.length */
    lastIndexOf(searchString: string, position?: number): number;

    /** Gets the number of characters in this string. */
    length: number;

    /**
     * Splits the string into lines.
     * If @param {string} separator is undefined Environment.newLine separator would be used.
     */
    lines(separator?: string): string[];

    /** Compare two strings in the current locale. */
    localeCompare(other: string): number;

    /** Returns a new string in which all occurrences of a specified string in the current instance are replaced with another specified string. */
    replace(oldValue: string, newValue: string): string;

    /**
     * Returns an array of substrings of this string. The substrings are determined by searching from left to right for occurrences
     * of separator; these occurrences are not part of any substring in the returned array, but serve to divide up the String value.
     * If limit is not undefined, then the output array is truncated so that it contains no more than limit elements.
     */
    split(separator: string, limit?: number): string[];

    /** Returns a section of a string, from 'start' up to, but not including, 'end' (which defaults to 'this.length'). */
    slice(start?: number, end?: number): string;

    /** Is prefix a prefix of string? */
    startsWith(prefix: string): boolean;

    /** Converts all the alphabetic characters in a string to lowercase. */
    toLowerCase(): string;

    /** Converts the first character in a string to lowercase. */
    toLowerCaseFirst(): string;

    /** Converts all the alphabetic characters in a string to uppercase. */
    toUpperCase(): string;

    /** Converts the first character in a string to uppercase. */
    toUpperCaseFirst(): string;

    /** Returns a string representation of a string. */
    toString(): string;

    /** Removes all leading and trailing occurrences which are in trimChars from this string (if omitted, trimChars defaults to white spaces). */
    trim(trimChars?: string): string;

    /** Removes all leading occurrences which are in trimChars from this string (if omitted, trimChars defaults to white spaces). */
    trimStart(trimChars?: string): string;

    /** Removes all trailing occurrences which are in trimChars from this string (if omitted, trimChars defaults to white spaces).*/
    trimEnd(trimChars?: string): string;

    /** Convert a string to an array of characters(i.e., singleton strings). */
    toArray(): string[];
}

/** An intrinsic object that serves as a string factory, in addition it has some useful functions */
namespace String {
    /** Generates string from a char code array. */
    export declare function fromCharCode(codes: number[]): string;

    /** Concatenates all strings with a separator. */
    export declare function join(separator: string, lines: string[]): string;

    /** Determines if a string is undefined or equal to the empty string. */
    export declare function isUndefinedOrEmpty(s: string): boolean;

    /**
     * Determines if a string is undefined, equal to the empty string,
     * or consists only of whitespace characters.
     */
    export declare function isUndefinedOrWhitespace(s: string): boolean;
}

//------------------------------------------------------------------------------
//   Number
//------------------------------------------------------------------------------

/**
 * A 32-bit signed integer (using two's complement to represent negative numbers).
 * Unline TypeScript or JavaScript Number is 32-bit integer but not a 64-bit floating point number.
 */
interface Number {
    //(===)( number, number ) : boolean;
    //(!==)( number, number ) : boolean;
    //(<)( number, number ) : boolean;
    //(<=)( number, number ) : boolean;
    //(>)( number, number ) : boolean;
    //(>=)( number, number ) : boolean;

    //(+)( number, number ) : number;
    //(-)( number, number ) : number;
    //(-)( number ) : number;
    //(*)( number, number ) : number;
    //--(/)( number, number ) : number;   // not supported, use Math.mod instead
    //--(%)( number, number ) : number;   // not supported, use math.div instead
    /**
      * Returns a string representation of an object.
      * @param radix Specifies a radix for converting numeric values to strings. This value is only used for numbers.
      */
    toString(radix?: number): string;

    /** Returns the primitive value of the specified object. */
    valueOf(): number;
}

/** An intrinsic object that provides basic number constants, and a factory */
namespace Number {
    /** Converts the string representation of a number to its 32-bit signed integer equivalent.
     * Undefined is returned if conversion fails.
     * The string must represent a number between -2,147,483,648 and 2,147,483,647 for the conversion to succeed.
     *
     */
    export declare function parseInt(s: string, radix?: number): number;
};

//-----------------------------------------------------------------------------
//  Pair
//-----------------------------------------------------------------------------

interface Pair<T, U> {
    first: T;
    second: U;
}

//-----------------------------------------------------------------------------
//  Grouping
//-----------------------------------------------------------------------------

interface Grouping<T, U> {
    key: T;
    values: U[];
}

//=============================================================================
//  Collections
//=============================================================================

//-----------------------------------------------------------------------------
//  Array
//-----------------------------------------------------------------------------

interface ElemWithStateResult<T, U> {
    elem: T;
    state: U;
}

interface ArrayWithStateResult<T, U> {
    elems: T[];
    state: U;
}

/** Immutable arrays of type T. */
// Use names from lib.d.ts (remove all LINQ-like methods out of there!)
interface Array<T> {
    /** (Read Only) Returns the number of elements in this array. */
    length: number;

    /** Returns true iff the length of this array is 0.  */
    isEmpty(): boolean;

    /** (Read Only) Returns the element of this array at given position 'n'.  Throws error if 'n' is out of bounds. */
    [n: number]: T;

    /** Returns a string representation of this array. */
    toString(): string;

    /** Creates a new array containing elements of this array appended with elements given in 'items'. */
    push(...items: T[]): T[];

    /**
     * Combines two or more arrays.
     */
    concat<U extends T[]>(...items: U[]): T[];

    /**
     * Combines two or more arrays.
     */
    concat(...items: T[]): T[];

    /**
     * Adds all the elements of an array separated by the specified separator string.
     * @param separator A string used to separate one element of an array from the next in the resulting String. If omitted, the array elements are separated with a comma.
     */
    join(separator?: string): string;

    /**
     * Reverses the elements in an Array.
     */
    reverse(): T[];

    /**
     * Removes the first element from an array and returns a tuple with shifted element and new array (without shifted element).
     */
    shift(): [T, T[]];

    /**
     * Returns a section of an array.
     * @param start The beginning of the specified portion of the array.
     * @param end The end of the specified portion of the array.
     */
    slice(start?: number, end?: number): T[];

    /**
     * Sorts an array.
     * @param compareFn The name of the function used to determine the order of the elements. If omitted, the elements are sorted in ascending, ASCII character order.
     */
    sort(compareFn?: (a: T, b: T) => number): T[];

    /**
     * Removes elements from an array and, if necessary, inserts new elements in their place, returning the deleted elements.
     * @param start The zero-based location in the array from which to start removing elements.
     */
    splice(start: number): T[];

    /**
     * Removes elements from an array and, if necessary, inserts new elements in their place, returning the deleted elements.
     * @param start The zero-based location in the array from which to start removing elements.
     * @param deleteCount The number of elements to remove.
     * @param items Elements to insert into the array in place of the deleted elements.
     */
    splice(start: number, deleteCount: number, ...items: T[]): T[];

    /**
     * Inserts new elements at the start of an array.
     * @param items  Elements to insert at the start of the Array.
     */
    unshift(...items: T[]): T[];

    /**
     * Returns the index of the first occurrence of a value in an array.
     * @param searchElement The value to locate in the array.
     * @param fromIndex The array index at which to begin the search. If fromIndex is omitted, the search starts at index 0.
     * @param callbackfn An optional predicate that can be used for custom comparison.
     */
    indexOf(searchElement: T, fromIndex?: number, callbackfn?: (value: T, index: number, array: T[]) => boolean): number;

    /**
     * Returns the index of the last occurrence of a specified value in an array.
     * @param searchElement The value to locate in the array.
     * @param fromIndex The array index at which to begin the search. If fromIndex is omitted, the search starts at the last index in the array.
     * @param callbackfn An optional predicate that can be used for custom comparison.
     */
    lastIndexOf(searchElement: T, fromIndex?: number, callbackfn?: (value: T, index: number, array: T[]) => boolean): number;

    /**
     * Determines whether all the members of an array satisfy the specified test.
     * @param callbackfn A function that accepts up to three arguments. The every method calls the callbackfn function for each element
     * in array1 until the callbackfn returns false, or until the end of the array.
     * @param thisArg An object to which the this keyword can refer in the callbackfn function. If thisArg is omitted, undefined is used as the this value.
     */
    every(callbackfn: (value: T, index: number, array: T[]) => boolean, thisArg?: any): boolean;

    /**
     * Determines whether the specified callback function returns true for any element of an array.
     * @param callbackfn A function that accepts up to three arguments. The some method calls the
     *        callbackfn function for each element in array1 until the callbackfn returns true, or
     *        until the end of the array.
     */
    some(callbackfn: (value: T, index: number, array: T[]) => boolean): boolean;

    /**
     * Determines whether the specified callback function returns true for every element of an array.
     * @param callbackfn A function that accepts up to three arguments: (1) array element, (2) index of
     *        the element, and (3) the array itself. The all method calls the callbackfn function for
     *        each element in array until the callbackfn returns false, or until the end of the array.
     */
    all(callbackfn: (value: T, index: number, array: T[]) => boolean): boolean;

    /**
     * Find the first element satisfying some predicate, undefined if element is not in array.
     */
    //TODO: currently not implemented
    //find(callbackfn: (value: T, index: number, array: T[]) => boolean): T;

    /**
     * Performs the specified action for each element in an array.
     * @param callbackfn  A function that accepts up to three arguments. forEach calls the callbackfn function one time for each element in the array.
     * @param thisArg  An object to which the this keyword can refer in the callbackfn function. If thisArg is omitted, undefined is used as the this value.
     */
    forEach(callbackfn: (value: T, index: number, array: T[]) => void, thisArg?: any): void;

    /**
     * Calls a defined callback function on each element of an array, and returns an array that contains the results.
     * @param callbackfn A function that accepts up to three arguments. The map method calls the callbackfn function one time for each element in the array.
     * @param thisArg An object to which the this keyword can refer in the callbackfn function. If thisArg is omitted, undefined is used as the this value.
      */
    map<U>(callbackfn: (value: T, index: number, array: T[]) => U, thisArg?: any): U[];

    /**
     * Calls a defined callback function on each element of an array, and returns an array that contains concatenated results.
     */
    mapMany<U>(callbackfn: (value: T, index: number, array: T[]) => U[]): U[];

    /**
     * Calls a defined callback function on each element of an array, and returns an array that contains the non-undefined results.
     * @param callbackfn A function that accepts up to three arguments. The map method calls the callbackfn function one time for each element in the array.
     * @param thisArg An object to which the this keyword can refer in the callbackfn function. If thisArg is omitted, undefined is used as the this value.
      */
    mapDefined<U>(callbackfn: (value: T, index: number, array: T[]) => U, thisArg?: any): U[];

    /**
     * Calls a defined callback function on each element of an array, and returns an array that contains the non-undefined results.
     * @param callbackfn A function that accepts up to three arguments. The map method calls the callbackfn function one time for each element in the array.
     * @param thisArg An object to which the this keyword can refer in the callbackfn function. If thisArg is omitted, undefined is used as the this value.
      */
    mapWithState<U, S>(callbackfn: (value: T, index: number, array: T[]) => ElemWithStateResult<U, S>, state: S, thisArg?: any): ArrayWithStateResult<U, S>;

    /**
     * Calls a defined callback function on each element of an array, and returns an array that contains concatenated results along with the final state.
     */
    mapManyWithState<U, S>(callbackfn: (state: S, value: T, index: number, array: T[]) => ArrayWithStateResult<U, S>, state: S): ArrayWithStateResult<U, S>;

    /**
     * Group by.
     */
    groupBy<U>(keySelectorFn: (value: T, array: T[]) => U): {key: U, values: T[]}[];

    /**
     * Returns a new array containing all elements of this array with duplicates removed. Order is not preserved.
     * This method is more efficient than unique(). Use this method if order is irrelevant.
     */
    toSet(): T[];

    /**
     * Returns a new array containing all elements of this array with duplicates removed. Order is preserved.
     */
    unique(): T[];
    /**
     * Returns the elements of an array that meet the condition specified in a callback function.
     * @param callbackfn A function that accepts up to three arguments. The filter method calls the callbackfn function one time for each element in the array.
     * @param thisArg An object to which the this keyword can refer in the callbackfn function. If thisArg is omitted, undefined is used as the this value.
     */
    filter(callbackfn: (value: T, index: number, array: T[]) => boolean, thisArg?: any): T[];

    /**
     * Calls the specified callback function for all the elements in an array. The return value of the callback function is the accumulated result, and is provided as an argument in the next call to the callback function.
     * @param callbackfn A function that accepts up to four arguments. The reduce method calls the callbackfn function one time for each element in the array.
     * @param initialValue If initialValue is specified, it is used as the initial value to start the accumulation. The first call to the callbackfn function provides this value as an argument instead of an array value.
     */
    reduce(callbackfn: (previousValue: T, currentValue: T, currentIndex: number, array: T[]) => T, initialValue?: T): T;

    /**
     * Calls the specified callback function for all the elements in an array. The return value of the callback function is the accumulated result, and is provided as an argument in the next call to the callback function.
     * @param callbackfn A function that accepts up to four arguments. The reduce method calls the callbackfn function one time for each element in the array.
     * @param initialValue If initialValue is specified, it is used as the initial value to start the accumulation. The first call to the callbackfn function provides this value as an argument instead of an array value.
     */
    reduce<U>(callbackfn: (previousValue: U, currentValue: T, currentIndex: number, array: T[]) => U, initialValue: U): U;

    /**
     * Calls the specified callback function for all the elements in an array, in descending order. The return value of the callback function is the accumulated result, and is provided as an argument in the next call to the callback function.
     * @param callbackfn A function that accepts up to four arguments. The reduceRight method calls the callbackfn function one time for each element in the array.
     * @param initialValue If initialValue is specified, it is used as the initial value to start the accumulation. The first call to the callbackfn function provides this value as an argument instead of an array value.
     */
    reduceRight(callbackfn: (previousValue: T, currentValue: T, currentIndex: number, array: T[]) => T, initialValue?: T): T;

    /**
     * Calls the specified callback function for all the elements in an array, in descending order. The return value of the callback function is the accumulated result, and is provided as an argument in the next call to the callback function.
     * @param callbackfn A function that accepts up to four arguments. The reduceRight method calls the callbackfn function one time for each element in the array.
     * @param initialValue If initialValue is specified, it is used as the initial value to start the accumulation. The first call to the callbackfn function provides this value as an argument instead of an array value.
     */
    reduceRight<U>(callbackfn: (previousValue: U, currentValue: T, currentIndex: number, array: T[]) => U, initialValue: U): U;

    /**
     * Zip two lists together by apply a function f to all corresponding elements. The returned array is only as long as the smaller input list.
     */
    zipWith<U, V>(array: U[], fun: (t: T, v: U) => V): V[];

    /**
     * Unzip a list of pairs into two lists using first and second selector.
     */
    unzipWith<U, V>(firstSelector: (t: T) => U, secondSelector: (t: T) => V): Pair<U, V>[];

    /**
     * Returns an array identical to this array, but with a custom merge function that appends to the returned array
     * Note: append is the default merging mechanism for arrays
     */
    appendWhenMerged() : Array<T>;

    /**
     * Returns an array identical to this array, but with a custom merge function that prepends to the returned array
     */
    prependWhenMerged() : Array<T>;

    /**
     * Returns an array identical to this array, but with a custom merge function that replaces the returned array
     */
    replaceWhenMerged() : Array<T>;

}

/** An intrinsic object that represents an array factory . */
namespace Array {
    /** Returns an integer array of increasing elements from 'start' to 'stop' (including both 'start' and 'stop'). If 'start > stop' the function returns the empty list. */
    export declare function range(start: number, stop: number, step?: number): number[];

    /** A new array of length n with initial elements default. */
    export declare function array<T>(n: number, defaultValue?: T): T[];
};

//=============================================================================
//  Miscellaneous
//=============================================================================

//-----------------------------------------------------------------------------
//  Math
//------------------------------------------------------------------------------

/** An intrinsic object that provides basic mathematics functionality. */
namespace Math {
    export declare function abs(x: number): number;

    /** Returns the sum of a set of supplied values, 0 if set is empty */
    export declare function sum(...values: number[]): number;

    /** Returns the maximum of a set of supplied values, 0 if set is empty */
    export declare function max(...values: number[]): number;

    /** Returns the mimimum of a set of supplied values, 0 if set is empty */
    export declare function min(...values: number[]): number;

    /** Returns the value of a base expression taken to a specified power. */
    export declare function pow(base: number, exponent: number): number;

    /** Euclidean-0 modulus */
    export declare function mod(dividend: number, divisor: number): number;

    /** Euclidian division */
    export declare function div(dividend: number, divisor: number): number;
}

/** Type that only allows one value */
interface Unit {
    __unitBrand: any;
}

namespace Unit {
    /** Returns the (only) value of type Unit*/
    export declare function unit(): Unit;
}