// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/** 
 * An intrinsic object that provides contract validation support for build specs.
 */
namespace Contract {
    /** 
     * Checks whether the condition holds true. 
     * If so, it reports an error wit the given message and fails further evaluation.
     * This check should be used to validate arguments of a function. 
     */
    export declare function requires(condition: boolean, message?: string): void;

    /** 
     * Checks whether the condition holds true. 
     * If so, it reports an error wit the given message and fails further evaluation.
     * This check should be used to validate internal state of your code. 
     * 
     * This method returns any as to not interfere with type inference and allow its use internary expressions.
     */
    export declare function assert(condition: boolean, message?: string) :  any;

    /** 
     * Reports an error with the given message and fails further evaluation. 
     * This check should be used to report errors to the users.
     * 
     * This method returns any as to not interfere with type inference and allow its use internary expressions.
     */
    export declare function fail(message: string): any;

    /** 
     * Reports a warning with the given message. 
     */
    export declare function warn(message: string): void;
};
