// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as XUnit from "DotNetCore.XUnit";
import * as DotNet from "DotNetCore.DotNetCoreRunner";

export function getDefaultXunitArgs(testAssembly: File, outDir: Directory): XUnit.Arguments {
    return <XUnit.Arguments>{
        testAssembly: undefined,
        parallel: "none",
        noColor: true,
        noAppDomain: true,
        noLogo: true,
        noShadow: true,
        xmlFile: p`${outDir}/${testAssembly.name.concat('.xunit.results.xml')}`
    };
}

export interface Arguments extends XUnit.Arguments {
    categories?: string[];
    noCategories?: string[];
    runSuppliedCategoriesOnly?: boolean;
}

export function runXunit(args: Arguments): DerivedFile[] {
    Contract.requires(args !== undefined);
    Contract.requires(args.testAssembly !== undefined);

    const categories = args.categories || [];
    const noCategories = args.noCategories || [];
    if (categories.length > 0) {
        const traitResults = categories.mapMany(cat => runSingleXunitInstance(args.override<Arguments>({traits: [categoryToTrait(cat)], noTraits: noCategories.map(cat => categoryToTrait(cat))})));
        const noTraitResults = args.runSuppliedCategoriesOnly
            ? []
            : runSingleXunitInstance(args.override<Arguments>({noTraits: [...categories, ...noCategories].map(categoryToTrait)}));
        return [
            ...traitResults,
            ...noTraitResults
        ];
    } else {
        return runSingleXunitInstance(args.override<Arguments>({noTraits: noCategories.map(cat => categoryToTrait(cat))}));
    }
}

function categoryToTrait(cat: string) {
    return {name: "Category", value: cat};
};

function runSingleXunitInstance(args: Arguments): DerivedFile[] {
    const traitHint =
        (args.traits || []).length > 0 ? "Traits-" + args.traits.map(t => t.value).join("_") :
        (args.noTraits || []).length > 0 ? "Rest" : "ALL";

    args = args.merge<Arguments>({noTraits: ["WindowsOSOnly", "QTestSkip", "Performance", "SkipDotNetCore", ...args.noTraits].map(categoryToTrait)});

    const outDir = Context.getNewOutputDirectory("xunit");
    const finalXunitArgs = getDefaultXunitArgs(args.testAssembly, outDir).override<XUnit.Arguments>(args);

    // NOTE: our Mac validation (i.e., the 'test_runner.sh') depends on this line being printed out
    Debug.writeLine(`=== Scheduled xunit test: '${Debug.dumpData(finalXunitArgs.xmlFile)}'`);

    const result = DotNet.execute({
        pathToApplication: f`${args.testAssembly.parent}/xunit.console.dll`,
        applicationArguments: XUnit.commandLineArgs(finalXunitArgs),
        executeArgsOverride: <any>{
            consoleOutput: p`${outDir}/${args.testAssembly.name.concat('.xunit-out.txt')}`,
            description: `[XUnit] ${args.testAssembly.name} (${traitHint})`,
            environmentVariables: [
                {name: "TestOutputDir", value: outDir},
                // TODO:
                // In windows we have a whitelist of default variables that get passed through and a default set of untracked
                // scopes a flag in the ExecutableDeployment is toggled. Once we learn what the patterns are, we should lift
                // these boilerplate settings to something common that's easily configurable or just the default
                ...passThroughEnvVars([
                    "HOME",
                    "TMPDIR",
                    "USER"
                ])
            ],
            dependencies: [
                ...globR(d`${args.testAssembly.parent}`, "*"),
                f`/bin/sh`,
                f`/bin/ls`,
                f`/bin/cat`,
            ],
            unsafe: {
                untrackedScopes: [
                    outDir,
                    d`/dev`,
                    d`/private`,
                    d`/usr`,
                    d`/var`,
                    d`/System/Library`,
                    d`/AppleInternal`,
                    d`/tmp`,
                    d`/Library/Preferences/Logging`,
                    d`/cores`
                ],
                untrackedPaths: [
                    ...(Environment.hasVariable("HOME") ? [
                        f`${Environment.getDirectoryValue("HOME")}/.CFUserTextEncoding`
                    ] : [])
                ]
            }
        }
    });
    return result.getOutputFiles();
}

function passThroughEnvVars(envVarNames: string[]): Transformer.EnvironmentVariable[] {
    return envVarNames
        .mapMany(envVarName => Environment.hasVariable(envVarName)
            ? [ {name: envVarName, value: Environment.getStringValue(envVarName)} ]
            : []);
}
