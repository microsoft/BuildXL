// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const source = "https://pkgs.dev.azure.com/1essharedassets/_packaging/BuildXL/nuget/v3/index.json";

// Checks a downloaded or reused NuGet payload has the requested identity and essential engine/SDK paths
// before exposing it as the installed build/<rid> directory or reusing an existing installation.
function validateDeployment(directory, id, version, extension) {
    const metadata = fs.readFileSync(path.join(directory, `${id}.nuspec`), "utf8");
    if (!new RegExp(`<id>\\s*${id.replace(".", "\\.")}\\s*</id>`, "i").test(metadata) ||
        !metadata.includes(`<version>${version}</version>`)) {
        throw new Error(`Expected ${id} ${version}; the installed NuGet package has a different identity.`);
    }
    for (const file of [
        `bxl${extension}`, `AdoBuildRunner${extension}`, "bxl.runtimeconfig.json",
        "BuildXL.Deployment.manifest", "Sdk/Sdk.Prelude/package.config.dsc",
        "Sdk/Sdk.Transformers/package.config.dsc"
    ]) {
        if (!fs.statSync(path.join(directory, ...file.split("/"))).isFile()) {
            throw new Error(`NuGet installation is missing ${file}.`);
        }
    }
    if (!fs.statSync(path.join(directory, "Sdk", "Sdk.QTest")).isDirectory()) {
        throw new Error("NuGet installation is missing the internal QTest SDK.");
    }
}

function install(packageRoot = __dirname, options = {}) {
    const platform = options.platform || process.platform;
    const arch = options.arch || process.arch;
    // TODO: Extend the runtime and npm-package mappings when macOS npm support is introduced.
    const runtime = platform === "win32" ? "win-x64" : platform === "linux" ? "linux-x64" : undefined;
    const name = platform === "win32" ? "@microsoft/buildxl-internal" : "@microsoft/buildxl-linux-internal";
    const manifest = JSON.parse(fs.readFileSync(path.join(packageRoot, "package.json"), "utf8"));
    if (!runtime || arch !== "x64" || manifest.name !== name) {
        throw new Error("This internal BuildXL npm package requires its matching Windows/Linux x64 host.");
    }
    const version = manifest.version;
    const exactVersion = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/.exec(version);
    if (!exactVersion || exactVersion[0] !== version ||
        (exactVersion[4] && exactVersion[4].split(".").some(part => /^0\d+$/.test(part)))) {
        throw new Error("The npm package must have an exact release version matching its published NuGet distribution.");
    }
    const id = `BuildXL.${runtime}`;
    const extension = platform === "win32" ? ".exe" : "";
    const build = path.join(packageRoot, "build");
    const target = path.join(build, runtime);
    fs.mkdirSync(build, { recursive: true });
    // Serialize installers through validation and publication, including the final check/rename window.
    const lock = path.join(build, `.${runtime}.install.lock`);
    try {
        fs.mkdirSync(lock);
    } catch (error) {
        if (error.code === "EEXIST") {
            throw new Error(`Another BuildXL npm installation is using ${target}. Retry when it finishes; if it was interrupted, remove only ${lock} after confirming no installation is running.`);
        }
        throw error;
    }
    let staging;
    try {
        if (fs.existsSync(target)) {
            validateDeployment(target, id, version, extension);
            console.log(`BuildXL npm: reusing ${id} ${version}.`);
            return target;
        }
        staging = fs.mkdtempSync(path.join(build, ".nuget-install-"));
        const config = path.join(staging, "NuGet.Config");
        fs.writeFileSync(config, "<configuration><packageSources><clear /></packageSources></configuration>");
        const client = platform === "win32" ? "nuget.exe" : "nuget";
        console.log(`BuildXL npm: installing ${id} ${version} from the internal BuildXL feed.`);
        const result = (options.spawnSync || spawnSync)(client, [
            "install", id, "-Version", version, "-Source", source,
            "-OutputDirectory", staging, "-ExcludeVersion", "-DirectDownload",
            "-PackageSaveMode", "nuspec", "-NonInteractive", "-ConfigFile", config, "-Verbosity", "normal"
        ], { cwd: packageRoot, stdio: "inherit", shell: false });
        if (result.error) {
            throw new Error(`Cannot run ${client} (${result.error.code}). Install a runnable NuGet CLI on PATH; Linux also needs its Mono/runtime prerequisites. No .NET SDK is required.`);
        }
        if (result.status !== 0) {
            const error = new Error(`NuGet install failed (${result.signal || `exit code ${result.status}`}). Check the output above, exact release availability, and Azure Artifacts feed authentication.`);
            error.exitCode = result.status || 1;
            throw error;
        }
        // -ExcludeVersion still creates a PackageId directory; move that entire tree, not selected files.
        const extracted = path.join(staging, id);
        validateDeployment(extracted, id, version, extension);
        if (platform === "linux") {
            // npm sets modes for bxl/AdoBuildRunner via bin; internal helpers still need execution bits.
            // CODESYNC: bxl.sh setExecutablePermissions, excluding the npm bin targets.
            for (const executable of [
                "bxlanalyzer", "bxlcacheanalyzer", "bxlScriptAnalyzer",
                "BxlPipGraphFragmentGenerator", "ContentStoreApp",
                "Downloader", "Extractor", "NugetDownloader", "SandboxedProcessExecutor",
                "tools/NinjaGraphBuilder/NinjaGraphBuilder",
                "tools/MsBuildGraphBuilder/dotnetcore/ProjectGraphBuilder"
            ]) {
                const file = path.join(extracted, ...executable.split("/"));
                if (fs.existsSync(file)) {
                    const stat = fs.statSync(file);
                    fs.chmodSync(file, stat.mode | 0o500);
                }
            }
        }
        if (fs.existsSync(target)) {
            throw new Error("The BuildXL target appeared during installation; refusing to replace existing files. Retry npm rebuild after the other install finishes.");
        }
        fs.renameSync(extracted, target);
        return target;
    } finally {
        try {
            if (staging) {
                fs.rmSync(staging, { recursive: true, force: true });
            }
        } finally {
            fs.rmdirSync(lock);
        }
    }
}

if (require.main === module) {
    try {
        install();
    } catch (error) {
        console.error(`BuildXL npm installation failed: ${error.message}`);
        process.exitCode = error.exitCode || 1;
    }
}

module.exports = { install };
