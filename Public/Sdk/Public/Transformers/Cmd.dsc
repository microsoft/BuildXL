// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

export namespace Cmd {
    @@public
    export const defaultArgumentSeparator : string = " ";

    // TODO: Argument validation are based on custom function isNotNullOrEmpty, because currently interpreter does not support !! pattern.
    function isNullOrEmpty(value: string): boolean {
        return value === undefined || value.length === 0;
    }

    /**
     * Adds some literal text to the command-line for the process.
     * This text is inserted as-is with no automatic quoting or any other processing except for the fact that text is
     * separated from other text by whitespace.
     *
     * Example:
     *  Cmd.rawArgument("/arg value with space") => "tool /arg value with space"
     *  Cmd.option("/arg ", "value with space") => "tool /arg 'value with space'"
     */
    @@public
    export function rawArgument(value: string): Argument {
        Contract.requires(!isNullOrEmpty(value), "value should not be undefined or empty");

        return {
            name: undefined,
            value: createPrimitive(value, ArgumentKind.rawText),
        };
    }

    /**
     * Adds a flag argument to the tool command lines if 'flagValue' is true.
     * Example: Cmd.flag("/nologo", arg.noLogo) => /nologo argument would be added only when arg.noLogo is true.
     */
    @@public
    export function flag(name: string, flagValue: boolean): Argument {
        Contract.requires(!isNullOrEmpty(name), "name should not be undefined or empty");

        if (flagValue === undefined || flagValue === false) {
            return undefined;
        }

        return {
            name: name,
            value: createPrimitive(undefined, ArgumentKind.flag),
        };
    }

    @@public
    export function optionalBooleanFlag(name: string, boolValue: boolean, trueFlag: string, falseFlag: string): Argument {
        Contract.requires(!isNullOrEmpty(name), "name should not be undefined or empty");

        if (boolValue === undefined) return undefined;

        let flag = boolValue ? trueFlag : falseFlag;
        return {
            name: name,
            value: (flag === "") ? createPrimitive(undefined, ArgumentKind.flag) : flag,
        };
    }

    /**
     * Creates an argument with '-' if boolValue is false, and the argument with '+' if true.
     * If enableNeedsNoPlus is set and boolValue is true, will just return the argument.
     * If boolValue is undefined 'undefined' would be returned.
     *
     * Example:
     *  Cmd.sign("/debug", arg.debug) => "tool /debug+" if arg.debug is true, "tool /debug-" if arg.debug is false.
     *  Cmd.sign("/opt", arg.optimize, true) => "tool /opt" if arg.optimize is true, "tool /opt-" if arg.optimize is false.
     */
    @@public
    export function sign(name: string, boolValue: boolean, enableNeedsNoPlus?: boolean): Argument {
        return optionalBooleanFlag(name, boolValue, enableNeedsNoPlus? "" : "+", "-");
    }

    /**
     * Specifies whether the process can use response files.
     * If the argument is true, then it forces the remaining arguments into a response file. 
     * If false, then a response file is only used if the command-line for the tool exceeds the maximum allowed
     * by the system.
     * 
     * By default, the prefix for response file is "@". Some tools, like C# or C++ compiler accepts a response
     * file in the form of "@file.rsp", where "@" is the prefix.
     *
     * Example:
     *   let args: Argument[] = [
     *     Cmd.flag("/noLogo", args.noLogo),
     *     Cmd.startUsingResponseFile(),
     * ];
     */
    @@public
    export function startUsingResponseFile(force?: boolean): Argument {
        return startUsingResponseFileWithPrefix(undefined, force);
    }

    /**
     * Specifies whether the process can use response files like startUsingResponseFile, but also allows users to specify a prefix.
     * For example, C# or C++ compiler accepts a response file in the form of "@file.rsp", where "@" is the prefix.
     * If prefix is undefined, then the default is "@".
     *
     * Example:
     *   let args: Argument[] = [
     *     Cmd.flag("/noLogo", args.noLogo),
     *     Cmd.startUsingResponseFile("@", true),
     * ];
     */
    @@public
    export function startUsingResponseFileWithPrefix(prefix: string, force?: boolean): Argument {
        let forceStr = (force === undefined) ? undefined : (force ? "true" : "false");
        return {
            name: prefix,
            value: createPrimitive(forceStr, ArgumentKind.startUsingResponseFile),
        };
    }

    /**
     * Special factory method that creates an argument with a set of files.
     *
     * Example:
     *   Cmd.files(arg.sources) => tool source1 source2
     */
    @@public
    export function files(files: File[]): Argument {
        if (files === undefined || files.length === 0) {
            return undefined;
        }

        return {
            name: undefined,
            value: Artifact.inputs(files),
        };
    }

    /**
     * Creates regular unnamed command line argument with specified value.
     * If value is undefined the function will return 'undefined', and no arguments would be added to the tool's command line.
     *
     * Example:
     *   Cmd.argument(arg.sourceFileName) => "tool sourceFileName"
     */
    @@public
    export function argument(value: ArgumentValue): Argument {
        if (value === undefined) {
            return undefined;
        }

        return {
            name: undefined,
            value: value
        };
    }

    /**
     * Creates regular unnamed command line argument with specified values.
     * If value is 'undefined' the function will return 'undefined', and no arguments would be added to the tool's command line.
     *
     * Example:
     *   Cmd.args(["x", "y"]) => "tool x y"
     */
    @@public
    export function args(values: ArgumentValue[]): Argument {
        if (values === undefined) {
            return undefined;
        }

        return {
            name: undefined,
            value: values
        };
    }

    /**
     * Creates named command line option.
     * If value is 'undefined' the function will return 'undefined' and no arguments would be added to the tool's command line.
     *
     * Example:
     *   Cmd.option("/timeout:", 42) => "tool /timeout:42"
     *   Cmd.option("--timeout ", 42) => "tool --timeout 42"
     *   Cmd.option("/r:", Cmd.join(",", ['mscorlib.dll', 'system.dll'])) => "tool /r:mscorlib.dll,system.dll"
     */
    @@public
    export function option(name: string, value: ArgumentValue, condition?: boolean): Argument {
        Contract.requires(!isNullOrEmpty(name), "name should not be undefined or empty");

        if (value === undefined || condition === false) {
            return undefined;
        }

        return {
            name: name,
            value: value,
        };
    }

    /**
     * Creates named command line options with multiple values.
     * This function will create a special argument that would have multiple values with the same name.
     *
     * Example:
     *   Cmd.options("/r:", ['r1.dll', 'r2.dll']) => "tool /r:r1.dll /r:r2.dll"
     */
    @@public
    export function options(name: string, values: ArgumentValue[]): Argument {
        Contract.requires(!isNullOrEmpty(name), "name should not be undefined or empty");

        if (values === undefined || values.length === 0) {
            return undefined;
        }

        return {
            name: name,
            value: values,
        };
    }

    /**
     * Creates a CompoundArgumentValue instance with a separator.
     * This helper function is very useful for creating complex command line options that contains multiple values.
     */
    @@public
    export function join(separator: string, values: ArgumentValue[]): CompoundArgumentValue {
        if (values === undefined || values.length === 0) {
            return undefined;
        }

        return {
            values: values,
            separator: separator,
        };
    }

    @@public
    export function concat(values: ArgumentValue[]): CompoundArgumentValue {
        return join("", values);
    }

    function createPrimitive(value: PrimitiveValue, kind: ArgumentKind): PrimitiveArgument {
        return {
            value: value,
            kind: kind,
        };
    }
}
