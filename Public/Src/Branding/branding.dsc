// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

export const explicitSemanticVersion = "0.1.0";

export declare const qualifier: {};

@@public
export const company = "Microsoft";

@@public
export const shortProductName = "BuildXL";

@@public
export const longProductName = "Microsoft (R) Build Accelerator";

@@public
export const shortScriptName = "DScript";

@@public
export const mainExecutableName = "bxl.exe";

@@public
export const analyzerExecutableName = "bxlAnalyzer.exe";

@@public
export const iconFile = f`BuildXL.ico`;

@@public
export const pngFile = f`BuildXL.png`;

@@public
export const copyright = "© Microsoft Corporation. All rights reserved.";

const devBuildPreReleaseTag = "devBuild";

@@public 
export const semanticVersion = Environment.hasVariable("[BuildXL.Branding]SemanticVersion")
    ? Environment.getStringValue("[BuildXL.Branding]SemanticVersion")
    : explicitSemanticVersion;

@@public
export const prereleaseTag = Environment.hasVariable("[BuildXL.Branding]PrereleaseTag")
    ? Environment.getStringValue("[BuildXL.Branding]PrereleaseTag")
    : Environment.hasVariable("[BuildXL.Branding]SourceIdentification")
        ? undefined
        : devBuildPreReleaseTag;

@@public
export const version = prereleaseTag
    ? semanticVersion + "-" + prereleaseTag 
    : semanticVersion;

@@public
export const sourceIdentification = Environment.hasVariable("[BuildXL.Branding]SourceIdentification")
    ? Environment.getStringValue("[BuildXL.Branding]SourceIdentification")
    : "Developer Build";

@@public
export const productDescription = `${longProductName}. Build: ${version}, Version: [${sourceIdentification}]`;

/**
 * Some tools unfortunately say they support SemVer but they don't support the prerelease tag of semver.
 * A well known system with such a hole is the Visual Studio market place where we have to publish our
 * vscode package and azure devops extensions.
 */
@@public
export const versionNumberForToolsThatDontSupportPreReleaseTag = computeVersionNumberForToolsThatDontSupportPreReleaseTag();

/** See versionNumberForToolThatDoesntSupportPeeReleaseTag*/
function computeVersionNumberForToolsThatDontSupportPreReleaseTag()
{
    if (!prereleaseTag || prereleaseTag === devBuildPreReleaseTag)
    {
        // If the prerelease tag is not sp
        return semanticVersion;
    }

    let parts = prereleaseTag.split(".");

    let preReleasePatch = 0;
    let preReleaseSeq = 0;
    let preReleaseDateOffSet = 0;

    if (parts.length > 0)
    {
        let yyyymmdd = parts[0];
        if (yyyymmdd.length !== 8) {
            // Some Pr validations have other numbering schemes assume like a devBuild
            return semanticVersion;
        }
        let year = Number.parseInt(yyyymmdd.slice(0,4));
        let mm = Number.parseInt(yyyymmdd.slice(4,6));
        let dd = Number.parseInt(yyyymmdd.slice(6,8));

        if (!year || year < 2019) {
            Contract.fail("Expected prerelease tag for BuildXL to be <yyymmdd>.<seq>?.<patch>? Where yyyy is a number after 2019");
        }
        if (!mm || mm < 0 || mm > 12) {
            Contract.fail("Expected prerelease tag for BuildXL to be <yyymmdd>.<seq>?.<patch>? Where mm is a number between 0 and 12");
        }
        if (!dd || dd < 0 || dd > 31) {
            Contract.fail("Expected prerelease tag for BuildXL to be <yyymmdd>.<seq>?.<patch>? Where dd is a number between 0 and 31");
        }

        // Compute the date offset by calculating the days since Jan 1st 2019
        // Pessimistically assume 31 days per month and 12 months per year.
        preReleaseDateOffSet = dd + 31 * (mm + 12 * (year - 2019));
    }

    if (parts.length > 1)
    {
        preReleaseSeq = Number.parseInt(parts[1]);
        if (!preReleaseSeq || preReleaseSeq > 20) {
            Contract.fail("Expected prerelease tag for BuildXL to be <yyymmdd>.<seq>?.<patch>? Where seq is a number not expected to go beyond 20");
        }
    }

    if (parts.length > 2)
    {
        preReleasePatch = Number.parseInt(parts[2]);
        if (!preReleasePatch || preReleasePatch > 5) {
            Contract.fail("Expected prerelease tag for BuildXL to be <yyymmdd>.<seq>?.<patch>? Where patch is a number not expected to go beyond 5");
        }
    }

    let semVerParts = semanticVersion.split(".");
    
    // Assume max 5 patches per build, Assume max 20 builds per day, and a major patch and prerelease patch will not happen on the same version.
    let semVerPatch = Number.parseInt(semVerParts[2]);
    let combinedPatchVersion = semVerPatch + preReleasePatch + 5 * (preReleaseSeq + 20 * preReleaseDateOffSet);

    // This should give us untill 2022 to update the minor version of BuildXL before the patch will overflow 16-bit integer. 
    // Which some tools use to implement semver allthough the spec does not specify the size.
    return semVerParts[0] + "." + semVerParts[1] + "." + combinedPatchVersion;
}