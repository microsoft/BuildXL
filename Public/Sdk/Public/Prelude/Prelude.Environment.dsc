// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>
/// <reference path="Prelude.IO.dsc"/>

namespace Environment {
    /** 
     * get an environment variable that represents a boolean. 
     * Returns false if the value is not defined, if it is '0' or 'false'
     * Returns true if the value is set to '1' or 'true'
     * Fails if the value is any other value.
     */
    export declare function getFlag(name: string) : boolean;

    /** get the value of an environment variable */
    export declare function getStringValue(name: string) : string;

    /** get the boolean value of an environment variable */
    export declare function getBooleanValue(name: string) : boolean;

    /** get the number value of an environment variable */
    export declare function getNumberValue(name: string) : number;

    /** get the path value of an environment variable */
    export declare function getPathValue(name: string) : Path;

    /** get the path values, separated by a separator, from a string value of an environment variable */
    export declare function getPathValues(name: string, separator: string) : Path[];

    /** get the file value of an environment variable */
    export declare function getFileValue(name: string) : File;

    /** get the file values, separated by a separator, from a string value of an environment variable */
    export declare function getFileValues(name: string, separator: string) : File[];

    /** get the directory value of an environment variable */
    export declare function getDirectoryValue(name: string) : Directory;

    /** get the directory values, separated by a separator, from a string value of an environment variable */
    export declare function getDirectoryValues(name: string, separator: string) : Directory[];

    /** check if the specified environment variable exists */
    export declare function hasVariable(name: string) : boolean;

    /** Returns a string that represents new-line separator */
    export declare function newLine() : string;

    /**
     * Replaces the name of each environment variable embedded in the specified path with the string equivalent of the value of the variable.
     * Replacement only occurs for environment variables that are set. Unset environment variables are left unexpanded.
     * If the resulting expansion is not a valid path, an evaluation error is generated.
     */
    export declare function expandEnvironmentVariablesInPath(path: Path) : Path;
    
    /**
     * Replaces the name of each environment variable embedded in the specified string with the string equivalent of the value of the variable.
     * Replacement only occurs for environment variables that are set. Unset environment variables are left unexpanded.
     */
    export declare function expandEnvironmentVariablesInString(string: string) : string;
}
