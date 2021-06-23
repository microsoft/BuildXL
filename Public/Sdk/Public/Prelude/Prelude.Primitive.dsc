// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// Set of primitive types. These are required by the type checker and they are not supposed to be used directly.

interface Boolean {
    /** Returns the primitive value of the specified object. */
    valueOf(): boolean;
}

interface Function {
    /**
      * Calls the function, substituting the specified object for the this value of the function, and the specified array for the arguments of the function.
      * @param thisArg The object to be used as the this object.
      * @param argArray A set of arguments to be passed to the function.
      */
    apply(thisArg: any, argArray?: any): any;

    /**
      * Calls a method of an object, substituting another object for the current object.
      * @param thisArg The object to be used as the current object.
      * @param argArray A list of arguments to be passed to the method.
      */
    call(thisArg: any, ...argArray: any[]): any;

    /**
      * For a given function, creates a bound function that has the same body as the original function. 
      * The this object of the bound function is associated with the specified object, and has the specified initial parameters.
      * @param thisArg An object to which the this keyword can refer inside the new function.
      * @param argArray A list of arguments to be passed to the new function.
      */
    bind(thisArg: any, ...argArray: any[]): any;

    prototype: any;
    length: number;

    // Non-standard extensions
    arguments: any;
    caller: Function;
}

interface PromiseLike<T> {
    /**
    * Attaches callbacks for the resolution and/or rejection of the Promise.
    * @param onfulfilled The callback to execute when the Promise is resolved.
    * @param onrejected The callback to execute when the Promise is rejected.
    * @returns A Promise for the completion of which ever callback is executed.
    */
    then<TResult>(onfulfilled?: (value: T) => TResult | PromiseLike<TResult>, onrejected?: (reason: any) => TResult | PromiseLike<TResult>): PromiseLike<TResult>;
    then<TResult>(onfulfilled?: (value: T) => TResult | PromiseLike<TResult>, onrejected?: (reason: any) => void): PromiseLike<TResult>;
}

interface RegExp {
    /** 
      * Executes a search on a string using a regular expression pattern, and returns an array containing the results of that search.
      * @param string The String object or string literal on which to perform the search.
      */
    exec(string: string): RegExpExecArray;

    /** 
      * Returns a Boolean value that indicates whether or not a pattern exists in a searched string.
      * @param string String on which to perform the search.
      */
    test(string: string): boolean;

    /** Returns a copy of the text of the regular expression pattern. Read-only. The regExp argument is a Regular expression object. It can be a variable name or a literal. */
    source: string;

    /** Returns a Boolean value indicating the state of the global flag (g) used with a regular expression. Default is false. Read-only. */
    global: boolean;

    /** Returns a Boolean value indicating the state of the ignoreCase flag (i) used with a regular expression. Default is false. Read-only. */
    ignoreCase: boolean;

    /** Returns a Boolean value indicating the state of the multiline flag (m) used with a regular expression. Default is false. Read-only. */
    multiline: boolean;

    lastIndex: number;

    // Non-standard extensions
    compile(): RegExp;
}

interface RegExpExecArray extends Array<string> {
    index: number;
    input: string;
}

interface IArguments {
    [index: number]: any;
    length: number;
    callee: Function;
}

interface Promise<T> {
    /**
    * Attaches callbacks for the resolution and/or rejection of the Promise.
    * @param onfulfilled The callback to execute when the Promise is resolved.
    * @param onrejected The callback to execute when the Promise is rejected.
    * @returns A Promise for the completion of which ever callback is executed.
    */
    then<TResult>(onfulfilled?: (value: T) => TResult | PromiseLike<TResult>, onrejected?: (reason: any) => TResult | PromiseLike<TResult>): Promise<TResult>;
    then<TResult>(onfulfilled?: (value: T) => TResult | PromiseLike<TResult>, onrejected?: (reason: any) => void): Promise<TResult>;

    /**
     * Attaches a callback for only the rejection of the Promise.
     * @param onrejected The callback to execute when the Promise is rejected.
     * @returns A Promise for the completion of the callback.
     */
    catch(onrejected?: (reason: any) => T | PromiseLike<T>): Promise<T>;
    catch(onrejected?: (reason: any) => void): Promise<T>;
}

//------------------------------------------------------------------------------
//   StringBuilder
//------------------------------------------------------------------------------

/** Bulider pattern for constructing complex string in performance efficient way. */
interface StringBuilder {
    /** Appends a copy of the specified string to this instance. */
    append(value: string): StringBuilder;
    
    /** Appends a copy of the specified string to this instance followed by a newline. */
    appendLine(value?: string): StringBuilder;

    /** Appends a copy of the specified string multiple times to this instance. */
    appendRepeat(value: string, repeatCount: number): StringBuilder;

    /** Replaces all occurrences of a specified string in this instance with another specified string. */
    replace(oldValue: string, newValue: string): StringBuilder;

    /** Converts the value of this instance to a string and clears the content. */
    toString(): string;
}

/** An intrinsic object that serves as a StringBuilder factory. */
namespace StringBuilder {
    /** Factory method that creates an instance of StringBuilder */
    export declare function create(): StringBuilder;
}

/** 
 * Helper to conditionally add items to an array literal.
 * This function returns the items array if the condition is true, empty array otherwise.
 * Example: [
 *    1,
 *    2,
 *    ...addIf(true, 3, 4),
 *    ...addIf(false, 5, 6),
 * ]
 * results in: [
 *    1,
 *    2,
 *    3,
 *    4,
 * ]
 */
export declare function addIf<T>(condition: boolean, ...items: T[]) : T[];

/** 
 * Helper to conditionally add items to an array literal wher the items are computed dynamically.
 * This function calls the items callback when the condition is true and returns the result. It will return an empty array otherwise.
 * Example: [
 *    1,
 *    2,
 *    ...addIf(true, () => [3, 4]),
 *    ...addIf(false, () => [5, 6]),
 * ]
 * results in: [
 *    1,
 *    2,
 *    3,
 *    4,
 * ]
 */
export declare function addIfLazy<T>(condition: boolean, items: () => T[]) : T[];

/**
 * An arbitrary DScript expression that can be later evaluated and whose expected return type is T.
 */
interface LazyEval<T> {
  expression: string;
}