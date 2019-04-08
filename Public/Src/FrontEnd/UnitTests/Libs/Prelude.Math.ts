// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
