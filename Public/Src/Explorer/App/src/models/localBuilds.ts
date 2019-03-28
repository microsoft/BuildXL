// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as fs from "fs";
import * as path from "path";
import * as userhome from "userhome";
import * as Luxon from "luxon";

import * as References from "../models/references";

// TODO:This should be updated with a filewatcher that fires an event each time the builds change so we can have a live view

const buildtsvPath = path.join(userhome(), 'AppData', 'Local', 'BuildXL', 'builds.tsv');

export interface BuildDetails extends References.BuildRef {
  kind: "local",
  buildStartTime: Luxon.DateTime,
  primaryConfigFile: string,
  logsFolder: string,
  engineVersion: string,
  engineBinFolder: string,
  engineCommitId: string,

  // Dynamically computed details on background worker
  durationMs?: number,
  state?: "started" | "runningpips" | "passed" | "failed" | "removed",
}

export function getLocalBuilds() : BuildDetails[] {
  var allBuilds = loadBuildTsv(buildtsvPath);

  // TODO: We need to see which logs are still availalbe
  // TODO: We need a button to show all builds.
  var recentBuilds = allBuilds.reverse().slice(0, 15);
  return recentBuilds;
}

/**
 * Tries to find the build details
 */
export function tryGetBuildDetails(sessionId: string) : BuildDetails | undefined {
  for (var detail of getLocalBuilds()) {
    if (detail.sessionId === sessionId) {
      return detail;
    }
  }

  return undefined;
}

/**
 * Load and parses the tsv file.
 */
function loadBuildTsv(tsvPath: string) : BuildDetails[] {
  let text = fs.readFileSync(tsvPath, { encoding: "utf8" });
  
  let errors: string[] = [];
  let builds: BuildDetails[] = [];

  let lines = text.split("\n")
  lines.forEach((line, index) => {
    // skip empty line;
    if (line == "") {
      return;
    }

    var row = line.split('\t');
    
    if (row.length == 0) {
      // Skip invalid lines
      errors.push(`${tsvPath}(${index}): Error: Encountered invalid empty line`);
      return;
    }

    let lineVersion = row[0];
    let buildDetails : BuildDetails | undefined;
    switch (lineVersion) {
      case "0":
        buildDetails = parseV1(tsvPath, index, row, errors);
        break;
      default:
        // Skip unsupported line version
        errors.push(`${tsvPath}(${index}): Error: Unsupported line version '${lineVersion}' in builds.tsv for line '${line}'. Please upgarde the Build Explorer to a newer version.`);
        return;
    }
    
    if (buildDetails) {
      builds.push(buildDetails);
    }

    return undefined;
  });

  if (errors.length > 0) {
    throw "Error Parsing file:\n" + errors.join("\n");
  }

  return builds;
}


function parseV1(tsvPath: string, index: number, row: string[], errors: string[]) : BuildDetails | undefined {
  if (row.length != 8) {
    // Skip invalid lines
    errors.push(`${tsvPath}(${index}): Error: Invalid number of columns for version '0' in the tsv file.`);
    return;
  }

  let sessionId = row[1];
  if (!sessionId) {
    errors.push(`${tsvPath}(${index}): Error: Invalid SessionId.`);
    return;
  }

  let buildStartTime = Luxon.DateTime.fromISO(row[2], {locale: "utc"});
  if (!buildStartTime) {
    errors.push(`${tsvPath}(${index}): Error: Invalid buildStartTimeUtc.`);
    return;
  }

  let primaryConfigFile = row[3];
  if (!primaryConfigFile) {
    errors.push(`${tsvPath}(${index}): Error: Invalid primaryConfigFile.`);
    return;
  }

  let logsFolder = row[4];
  if (!logsFolder) {
    errors.push(`${tsvPath}(${index}): Error: Invalid logFolder.`);
    return;
  }

  let engineVersion = row[5];
  if (!engineVersion) {
    errors.push(`${tsvPath}(${index}): Error: Invalid engineVersion.`);
    return;
  }

  let engineBinFolder = row[6];
  if (!engineBinFolder) {
    errors.push(`${tsvPath}(${index}): Error: Invalid engineBinFolder.`);
    return;
  }

  let engineCommitId = row[7];
  if (!engineCommitId) {
    errors.push(`${tsvPath}(${index}): Error: Invalid engineCommitId.`);
    return;
  }

  return {
    sessionId: sessionId,
    kind: "local",
    buildStartTime: buildStartTime,
    primaryConfigFile: primaryConfigFile,
    logsFolder: logsFolder,
    engineVersion: engineVersion,
    engineBinFolder: engineBinFolder,
    engineCommitId: engineCommitId,
  }
}
