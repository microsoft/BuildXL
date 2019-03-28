// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

"use strict";

const fs = require('fs');
const path = require('path');
const child_process = require('child_process');

const _dbgEnabled = true;

if (!RunCommandWithCustomNpmRc()) {
    process.exit(1);
}

function RunCommandWithCustomNpmRc() {

    if (!process.env.NUGET_CREDENTIALPROVIDERS_PATH) {
        console.log("No 'NUGET_CREDENTIALPROVIDERS_PATH' environment variable found. Running regular command.");
        return runRegularCommand();
    }

    logDebug("Environment variables")
    for (var envVar in process.env) {
        logDebug(`${envVar} = ${process.env[envVar]}`);
    }

    const npmRcFile = ".npmrc";

    const urls = extractUrlsWithCredentialProviders(npmRcFile);
    if (urls == null || urls.length == 0) {
        console.log("No registry configuration found with 'useCredentialProvider=true'.");
        return runRegularCommand();
    }

    let authTokenConfigurationBlock = urls.map(url => getCredentialLine(url)).join("\n") + "\n";

    // Create an npmrc file in the parent directory, to not modify the existing running directory
    const parentNpmRcFile = path.join(process.cwd(), "..", npmRcFile);
    fs.writeFileSync(parentNpmRcFile, authTokenConfigurationBlock, { encoding: "utf8" });

    var success = runRegularCommand();

    // and delete temporary npmrc file.
    //fs.unlinkSync(parentNpmRcFile);

    logDebug(success ? "Done" : "Failed");

    return success;
}

function getCredentialLine(url) {
    logDebug("Getting credentials for: " + url);

    let fullUrl = "https:" + url;
    let providerArgs = [
        "-uri", fullUrl,
        "-noninteractive", // indicate non-interactive
        // For dev.Azure.com feeds
        "-tokenscopes", "vso.packaging",
        // Needed for Microsoft internal system
        "-secretName", "CBServiceAccts",
        //"-verbosity", "detailed"
    ];

    const providerPaths = process.env.NUGET_CREDENTIALPROVIDERS_PATH.split(";");
    logDebug("ProviderPaths: " + process.env.NUGET_CREDENTIALPROVIDERS_PATH);

    for (var providerPath of providerPaths) {
        const potentialFiles = fs.readdirSync(providerPath);
        var providerFiles = potentialFiles.filter(potentialFile => {
            let fileName = potentialFile.toLowerCase();
            return fileName.startsWith("credentialprovider") && fileName.endsWith(".exe");
        });

        for (var providerFile of providerFiles) {
            let providerExecutable = path.join(providerPath, providerFile);
            try {
                logDebug(`Launching: ${providerExecutable} ${providerArgs.join(" ")}`);

                let result = child_process.execFileSync(providerExecutable, providerArgs, { encoding: "utf8", maxBuffer: 8 * 1024 * 1024 })

                let succesResult = JSON.parse(result);
                let pwd = succesResult.Password;
                return url + ":_authToken=" + pwd;
            }
            catch (e) {

                let errorResult = JSON.parse(e.stdout);
                console.error(`Npm auth: Failed to get credentials for '${fullUrl}' using '${providerExecutable} ${providerArgs.join(" ")}': ${errorResult.Message}`);
            }
        }
    }

    return ""; // none found
}


function extractUrlsWithCredentialProviders(npmRcFile) {

    if (!fs.existsSync(npmRcFile)) {
        return [];
    }

    const npmRcFileContents = fs.readFileSync(npmRcFile, { encoding: "utf8" });

    const rePattern = new RegExp(/(.*):useCredentialProvider=true/g);
    const urls = npmRcFileContents.match(rePattern);
    return urls.map(url => url.substring(0, url.length - ":useCredentialProvider=true".length));
}


function runRegularCommand() {
    let args = [...process.argv];
    args.splice(0, 2);

    try {
        const result = child_process.spawnSync(process.argv[0], args, { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
        if (result.status !== 0) {
            console.log(result.stdout);
            console.error(result.stderr);

            if (fs.existsSync("yarn-error.log")) {
                console.log("Found Yarn error log:")
                console.log(fs.readFileSync(yarn - error.log, { encoding: "utf8" }));
            }

            return false;
        }

        console.log(result.stdout);
        return true;
    }
    catch (e) {
        console.error((e.output || ["SOMETHING WENT WRONG: " + JSON.stringify(e)]).join("\n"));
        return false;
    }
}

function logDebug(msg) {
    if (_dbgEnabled) {
        if (typeof msg === "string") {
            console.error("[YCPH]: " + msg);
        }
        else {
            console.error(msg);
        }
    }
}
