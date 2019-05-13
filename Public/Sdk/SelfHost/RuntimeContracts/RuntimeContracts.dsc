// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

export declare const qualifier: {
    configuration: "debug" | "release";
    targetFramework: "netcoreapp3.0" | "netstandard2.0" | "netstandard1.1" | "net472" | "net462" | "net461" | "net46" | "net452" | "net451" | "net45";
};

/** Configures which asserts should be checked at runtime. */
@@public
export const enum ContractsLevel {
    /** All assertions are disabled. */
    disabled = 0,
    /** Preconditions are enabled. */
    requires = 1 << 1,
    /** Postconditions are enabled. Currently not supported. */
    ensures = 1 << 2,
    /** Invariantes are enabled. */
    invariants = 1 << 3,
    /** Assertions (Contract.Assert and Contract.Assume) are enabled. */
    assertions = 1 << 4,

    // This is not valid today. Need to use a const value instead.
    // full = requires | ensures | invariants | assertions
}

export namespace ContractLevel {
    // Today we can't declare enum members with anything except numeric literals.
    // So we need to use a special namespace for common assertion levels.
    @public
    export const full: ContractsLevel = ContractsLevel.requires | ContractsLevel.ensures | ContractsLevel.invariants | ContractsLevel.assertions;
}

@@public
export function withRuntimeContracts(args: Managed.Arguments, contractsLevel?: ContractsLevel) : Managed.Arguments {
    const isDebug = qualifier.configuration === 'debug';

    return args.merge<Managed.Arguments>({
        defineConstants: getContractsSymbols(contractsLevel || ContractLevel.full, isDebug),
        references: [
            // Use .NETStandard as target framework, as its compatible with both .NET 4.7.2 and .NETCore
            importFrom("RuntimeContracts").withQualifier({targetFramework: 'netstandard2.0'}).pkg
        ],
        tools: {
            csc: {
                analyzers: [
                    ...dlls(importFrom("RuntimeContracts.Analyzer").withQualifier({targetFramework: 'netstandard1.3'}).pkg)
                ]
            },
        }});
}

export function getContractsSymbols(level: ContractsLevel, enableContractsQuantifiers?: boolean): string[] {
    let result: string[] = [];

    if (hasFlag(level, ContractsLevel.requires)) {
        result = result.push("CONTRACTS_LIGHT_PRECONDITIONS");
    }

    if (hasFlag(level, ContractsLevel.ensures)) {
        // Postconditions are not supported yet.
    }

    if (hasFlag(level, ContractsLevel.invariants)) {
        result = result.push("CONTRACTS_LIGHT_INVARIANTS");
    }

    if (hasFlag(level, ContractsLevel.assertions)) {
        result = result.push("CONTRACTS_LIGHT_ASSERTS");
    }

    if (enableContractsQuantifiers) {
        result = result.push("CONTRACTS_LIGHT_QUANTIFIERS");
    }

    return result;
}

/** Returns analyzers dll for RuntimeContracts nuget package. */
export function getAnalyzers() : Managed.Binary[] {
    return dlls(importFrom("RuntimeContracts.Analyzer").withQualifier({targetFramework: 'netstandard1.3'}).pkg);
}

function dlls(nugetPackage: NugetPackage): Managed.Binary[] {
    // Getting dlls from the 'cs' folder.
    // This is not 100% safe but good enough.

    // BuildXL should suport an overload for getContent function that takes a root.
    let contents = nugetPackage.contents;

    return contents
        .getContent()
        .filter(file => file.extension === a`.dll` && file.parent.name === a`cs`)
        .map(file => Managed.Factory.createBinary(contents, file));
}

function hasFlag(level: ContractsLevel, c: ContractsLevel): boolean {
    return ((level & c) === c);
}
