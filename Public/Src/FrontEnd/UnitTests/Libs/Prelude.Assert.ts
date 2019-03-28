// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Assert {
    export declare function fail(message: string) : void;
    export declare function isTrue(condition: boolean, message?: string) : void;
    export declare function isFalse(condition: boolean, message?: string) : void;
    export declare function areEqual<T>(left:T, right: T, message?: string) : void;
    export declare function notEqual<T>(left:T, right: T, message?: string) : void;
}
