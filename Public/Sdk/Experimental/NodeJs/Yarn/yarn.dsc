// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import {Node, Npm} from "Sdk.NodeJs";
import * as Deployment from "Sdk.Deployment";

const yarnModule = Npm.install({name: "yarn", version: "1.12.3"});

/**
 * This installs the yarn packages for the given project.
 * To ensure we don't write in the source tree this will copy the project into the output folder
 */
@@public
export function install(args: Arguments) : Result {
    const projectFolder = d`${Context.getNewOutputDirectory(
        `yarn-install`
    )}/${args.targetSubFolder || "."}`;
    
    const useAuthenticatedPackageFeed = args.authenticatedPackageFeed !== undefined;

    let lockFile = f`${args.projectFolder}/yarn.lock`;

    let inputs = [];
    let projectDeployment = Deployment.createFromDisk(
        args.projectFolder, 
        {
            excludeDirectories: [d`${args.projectFolder}/node_modules`],
            excludeFiles: useAuthenticatedPackageFeed 
                ? Set.create<File>(lockFile) 
                : undefined,
        }, 
        true/*recursive*/
    );
    
    if (useAuthenticatedPackageFeed) {

        const yarnLockDirectory = Context.getNewOutputDirectory("yarn-lock");
        let yarnContents = File
            .readAllText(lockFile, TextEncoding.utf8)
            .replace("registry.yarnpkg.com", args.authenticatedPackageFeed);
        let yarnLockFile = Transformer.writeAllText({
            outputPath: p`${yarnLockDirectory}/yarn.lock`, 
            text: yarnContents
        });
        let npmRcFile = Transformer.writeAllLines({
            outputPath: p`${yarnLockDirectory}/.npmrc`,
            lines: [
                `registry=https://${args.authenticatedPackageFeed}/`,
                `//${args.authenticatedPackageFeed}/:useCredentialProvider=true`,
                `always-auth=true`,
                `_auth=TOKEN`,
                "",
            ]
        });

        // if we use an authenticated feed we should add a .npmrc file.
        // as well as a yarn.lock file that has the feed replaced.
        projectDeployment = {
            contents: [
                npmRcFile,
                projectDeployment,
                yarnLockFile,
            ]
        };
    }

    let deployedProject = Deployment.deployToDisk({definition: projectDeployment, targetDirectory: projectFolder}).contents;
    
    const nodeModules = d`${projectFolder}/node_modules`;

    let arguments: Argument[] = [
        Cmd.argument(Artifact.none(p`${yarnModule.nodeModules.root}/yarn/lib/cli.js`)),
        Cmd.argument("install"),
        Cmd.flag("--prod=true", args.production !== false), // Defaults to true
        // Ideally we lock down yarn to not use any global settings from user settings, but unfortunately this prevents the .npmrc file in the parent folder from working
        //Cmd.argument("--no-default-rc"), 
        Cmd.argument("--frozen-lockfile"), // Don’t generate a yarn.lock lockfile and fail if an update is needed.
        Cmd.argument("--non-interactive"), // Disable interactive prompts, like when there’s an invalid version of a dependency
        Cmd.option("--network-concurrency", "1"), // Workaround for issues with tarball issues.
        Cmd.argument("--ignore-scripts"), // Prevent any scripts inside the pacakge from running.
        ...(args.privateCache ? [Cmd.option("--cache-folder ", Artifact.output(args.privateCache))] : []),
    ];
    
    let credentialProviderArguments = {};

    if (useAuthenticatedPackageFeed && Context.getCurrentHost().os === "win") {

        if (Environment.hasVariable("NUGET_CREDENTIALPROVIDERS_PATH")) {
            const nugetCredentialProviderPath = Environment.getDirectoryValue("NUGET_CREDENTIALPROVIDERS_PATH");
        
            const nugetCredentialProviderArguments = {
                arguments: [Cmd.argument(Artifact.input(f`yarnWithNugetCredentialProvider.js`))].prependWhenMerged(),
                environmentVariables: [{name: "NUGET_CREDENTIALPROVIDERS_PATH", value: nugetCredentialProviderPath.path}],
                unsafe: {
                    untrackedScopes: [
                        nugetCredentialProviderPath,
                        d`${Context.getMount("ProgramData").path}/microsoft/netFramework`, // Most cred providers are managed code so need these folders... 
                        d`${Context.getMount("LocalLow").path}/Microsoft/CryptnetFlushCache`, // Windows uses this location as a certificate cache
                        d`${Context.getMount("LocalLow").path}/Microsoft/CryptnetUrlCache`,
                        d`${Context.getMount("AppData").path}/Microsoft/Crypto/RSA`, // Windows uses this location as a certificate cache
                        d`${Context.getMount("AppData").path}/Microsoft/SystemCertificates/My/Certificates`, // Cache for certificats
                        d`${Context.getMount("AppData").path}/Microsoft/SystemCertificates/My/Keys`, // Cache for certificats
                        d`${Context.getMount("AppData").path}/Microsoft/VisualStudio Services/7.0/Cache`, // Cache for visaul studio services
                    ],
                },
            };

            credentialProviderArguments = credentialProviderArguments.merge(nugetCredentialProviderArguments);
        }

        if (Environment.hasVariable("QAUTHMATERIALROOT")) {
            const qAuthMaterialRoot = Environment.getDirectoryValue("QAUTHMATERIALROOT");

            const qAuthMaterial = {
                environmentVariables: [{name: "QAUTHMATERIALROOT", value: qAuthMaterialRoot.path}],
                unsafe: {
                    untrackedScopes: [
                        qAuthMaterialRoot
                    ],
                },
            };

            credentialProviderArguments = credentialProviderArguments.merge(qAuthMaterial);
        }

        credentialProviderArguments = credentialProviderArguments.merge({
            unsafe: {
                untrackedPaths: [
                    f`d:/app/autopilot.ini`,
                ],
                untrackedScopes: [
                    d`d:/data/AutoPilotData`,
                    d`d:/data/logs/AuthHelpers`,
                    d`d:/data/Q/AuthHelpers`,
                    d`d:/data/Q/QSecretsDPAPI`,
                    d`d:/data/Q/RegionConfig`,
                    d`d:/data/Q/TelemetryConfig`,
                    d`${Context.getMount("ProgramData").path}/Microsoft/Crypto`,
                ],
                passThroughEnvironmentVariables: [
                    "__CLOUDBUILD_AUTH_HELPER_ROOT__",
                    "__Q_DPAPI_Secrets_Dir",
                    "__CREDENTIAL_PROVIDER_LOG_DIR",
                ],
            }
        });
    }

    const result = Node.run(
        Object.merge<Transformer.ExecuteArguments>(
            {
                arguments: arguments,
                workingDirectory: projectFolder,
                dependencies: [
                    deployedProject,
                    yarnModule.nodeModules,
                ],
                acquireMutexes: ["yarn"], // Yarn has issues with multiple installs running at the same time, we have to limmit parallization here :(
                outputs: [
                    nodeModules,
                ],
                unsafe: {
                    untrackedPaths: [
                        f`${projectFolder.parent}/.npmrc`,
                        f`${Context.getMount("UserProfile").path}/.npmrc`,
                        f`${Context.getMount("UserProfile").path}/.yarnrc`,
                    ],
                    untrackedScopes: [
                        ...(args.privateCache ? [] : [
                            // We don't have a private cache so untrack the appdata cache folder
                            d`${Context.getMount("LocalAppData").path}/yarn`,
                        ]),
                    ],
                },
            },
            credentialProviderArguments
        )
    );
    
    return {
        projectFolder: <StaticContentDirectory>deployedProject,
        modulesFolder: <OpaqueDirectory>result.getOutputDirectory(nodeModules),
    };
}

@@public
export interface Arguments {
    projectFolder: Directory;
    production?: boolean;
    targetSubFolder?: string | PathAtom;
    privateCache?: Directory;
    /** Optional feed to use instead of 'https://registry.yarnpkg.com'. This assumes that restore has to be authenicated as well. */
    authenticatedPackageFeed?: string;
}

@@public
export interface Result {
    projectFolder: StaticContentDirectory;
    modulesFolder: OpaqueDirectory;
}