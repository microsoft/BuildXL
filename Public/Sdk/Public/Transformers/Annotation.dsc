// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

export namespace Annotation {
    @@public
    export type AnnotationResult = (a: any) => any;

    /** 
     * Placeholder that should be used as a body for ambient annotations.
     * 
     * DScript extends TypeScript language with support of ambient decorators (i.e. decorators with no additional behavior).
     * Such annotations could be used on type declarations and doesn't have any runtime semantics.
     * 
     * DScript uses the same set of rules that TypeScript has and requires all ambient decorators to return a function.
     * To simplify following this contract, every function that is expected to be used as an annotation should return annotationBody.
     */
    @@public
    export const annotationBody: AnnotationResult = dummyArg => dummyArg;
}
