// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Tool {
    /** The option value separation mode which describes whether how the parser must take care of the separator. */
    @@public
    export const enum OptionSeparationMode {
        /** The option separation mode is not set. */
        notSet = 0,

        /** Means that for scalar options option name and the value are not separated anyhow of the following form /[opt][value].
         * For example, the following string /pathB:\src represents an option named 'path' and value 'B:\src'. */
        notSupported,

        /** Means that for scalar options option name and the value maybe separated by an optional separator character and are of the following 
         * form /[opt][optional separator][value]. For example, the following strings /mode:strict and /modestrict represent an option named 'mode' 
        * and value 'strict'. */
        supported,

        /** Same as Supported but separator is required. */
        required
    }

    @@public
    export interface Options {
        /** The option name and value separation mode which describes whether how the parser must take care of the separator. */
        optionSeparationMode?: OptionSeparationMode;

        /**The option name and value separator character. */
        optionSeparator?: string;

        /** The value which indicates whether the option supports multiple values that follow single option name. */
        supportsMultipleValues?: boolean;

        /** The multiple values separator character. */
        multipleValueSeparator?: string;

        /** The value which indicates whether the option should be negated/toggled for boolean transformer arguments. */
        negateOption?: boolean;

        valueSeparator?: string;
    }

    /** The annotation for an option for a runner. It represents a mapping between a command
     * line option for a tool's executable and the runner's argument interface properties
     */
    @@public
    export function option(opt: string, moreOptions?: Options): Annotation.AnnotationResult {
        return Annotation.annotationBody;
    }

    /** The annotation for a runner. */
    @@public
    export function name(name: string): Annotation.AnnotationResult {
        return Annotation.annotationBody;
    }

    /** The annotation for a runner function declaration. */
    @@public
    export function runner(name: string): Annotation.AnnotationResult {
        return Annotation.annotationBody;
    }

    /** A structure representing some metadata for a builder. */
    @@public
    export interface BuilderMetadata {
        /** The builder name */
        name: string;

        /** The transitive closure of runners that are invoked by the builder. */
        invokesTransformers: string[];
    }

    /** The annotation for a builder function declaration. */
    @@public
    export function builder(metadata: BuilderMetadata): Annotation.AnnotationResult {
        return Annotation.annotationBody;
    }
}
