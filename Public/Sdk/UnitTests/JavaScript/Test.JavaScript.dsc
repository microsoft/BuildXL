// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import {Assert, Testing} from "Sdk.Testing";
import {Yarn, Npm, Rush} from "Sdk.JavaScript";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function runNpmInstall(){
        Testing.setBuildParameter("USERPROFILE", d`src/userprofile`.toDiagnosticString());
        const defaultInstall = Npm.runNpmInstall({
            nodeTool: {exe: f`path\to\node`},
            npmTool: {exe: f`path\to\npm`},
            targetFolder: d`out\target\folder`,
            package: {name: "@ms/test", version: "1.0.0"},
            npmCacheFolder: d`out\npm\cache`
        });
    }

    @@Testing.unitTest()
    export function runRushInstall(){
        Testing.setBuildParameter("USERPROFILE", d`src/userprofile`.toDiagnosticString());
        Testing.setMountPoint({
            name: a`Windows`,
            path: p`C:\Windows`,
            isReadable: true,
            isWritable: true,
            isSystem: true,
            isScrubbable: true,
            trackSourceFileChanges: true,
        });
        const defaultInstall = Rush.runRushInstall({
            nodeTool: {exe: f`path\to\node`},
            rushTool: {exe: f`path\to\rush`},
            repoRoot: d`out\src`,
            pnpmStorePath: d`out/path/to/pnpm/store`,
        });
    }

    @@Testing.unitTest()
    export function runYarnInstall(){
        Testing.setBuildParameter("USERPROFILE", d`src/userprofile`.toDiagnosticString());
        const defaultInstall = Yarn.runYarnInstall({
            nodeTool: {exe: f`path\to\node`},
            yarnTool: {exe: f`path\to\yarn`},
            repoRoot: d`out\src`,
            yarnCacheFolder: d`out/path/to/pnpm/store`,
            networkConcurrency: 40,
            networkTimeout: 300000
        });
    }
}
