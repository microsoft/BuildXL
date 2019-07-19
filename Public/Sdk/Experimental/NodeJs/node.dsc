// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

namespace Node {

    @@public
    export const tool = getNodeTool();
    
    @@public
    export const npmCli = getNpmCli();

    @@public
    export function run(args: Transformer.ExecuteArguments) : Transformer.ExecuteResult {
        // Node code can access any of the following user specific environment variables.
        const userSpecificEnvrionmentVariables = [
            "APPDATA",
            "LOCALAPPDATA",
            "USERPROFILE",
            "USERNAME",
            "HOMEDRIVE",
            "HOMEPATH",
            "INTERNETCACHE",
            "INTERNETHISTORY",
            "INETCOOKIES",
            "LOCALLOW",
        ];
        const execArgs = Object.merge<Transformer.ExecuteArguments>(
            {
                tool: tool,
                workingDirectory: tool.exe.parent,
                unsafe: {
                    passThroughEnvironmentVariables: userSpecificEnvrionmentVariables
                }
            },
            args
        );

        return Transformer.execute(execArgs);
    }

    function getNodeTool() : Transformer.ToolDefinition {
        const host = Context.getCurrentHost();
    
        Contract.assert(host.cpuArchitecture === "x64", "Only 64bit verisons supported.");
    
        let executable : RelativePath = undefined;
        let pkgContents : StaticDirectory = undefined;
        
        switch (host.os) {
            case "win":
                pkgContents = importFrom("NodeJs.win-x64").extracted;
                executable = r`node-v8.12.0-win-x64/node.exe`;
                break;
            case "macOS": 
                pkgContents = importFrom("NodeJs.osx-x64").extracted;
                executable = r`node-v8.12.0-darwin-x64/bin/node`;
                break;
            case "unix":
                pkgContents = importFrom("NodeJs.linux-x64").extracted;
                executable = r`node-v8.12.0-linux-arm64/bin/node`;
                break;
            default:
                Contract.fail(`The current NodeJs package doesn't support the current OS: ${host.os}. Esure you run on a supported OS -or- update the NodeJs package to have the version embdded.`);
        }
  
        return {
            exe: pkgContents.getFile(executable),
            runtimeDirectoryDependencies: [
                pkgContents,
            ],
            prepareTempDirectory: true,
            dependsOnWindowsDirectories: true,
            dependsOnAppDataDirectory: true,
        };
    }

    function getNpmCli() {
        const host = Context.getCurrentHost();
    
        Contract.assert(host.cpuArchitecture === "x64", "Only 64bit verisons supported.");
    
        let executable : RelativePath = undefined;
        let pkgContents : StaticDirectory = undefined;
        
        switch (host.os) {
            case "win":
                pkgContents = importFrom("NodeJs.win-x64").extracted;
                executable = r`node-v8.12.0-win-x64/node_modules/npm/bin/npm-cli.js`;
                break;
            case "macOS": 
                pkgContents = importFrom("NodeJs.osx-x64").extracted;
                executable = r`node-v8.12.0-darwin-x64/lib/node_modules/npm/bin/npm-cli.js`;
                break;
            case "unix":
                pkgContents = importFrom("NodeJs.linux-x64").extracted;
                executable = r`node-v8.12.0-linux-arm64/lib/node_modules/npm/bin/npm-cli.js`;
                break;
            default:
                Contract.fail(`The current NodeJs package doesn't support the current OS: ${host.os}. Esure you run on a supported OS -or- update the NodeJs package to have the version embdded.`);
        }

        return pkgContents.getFile(executable);
    }
}
