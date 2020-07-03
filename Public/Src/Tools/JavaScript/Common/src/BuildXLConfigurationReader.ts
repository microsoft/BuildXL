import * as path from 'path';
import * as fs from 'fs';

/**
 * The name of the configuration that decorates a JavaScript project with
 * bxl-specific information
 */
const bxlConfigurationFilename = "bxlconfig.json";

/**
 * Paths can start with this token to denote the root of the repo
 */
const workspaceDirStartToken = "<workspaceDir>/";

/**
 * The object that represents a BuildXL JavaScript configuration file for an associated JavaScript project
 */
export interface BuildXLConfiguration {
    /** Each path is interpreted as an output directory */
    outputDirectories: PathWithTargets[];
    /** Each path is interpreted as a source file */
    sourceFiles: PathWithTargets[];
}

/**
 * A path with optional target scripts where the given path applies
 * If targetScripts is not specified, the path applies to all target scripts defined in the corresponding
 * package.json
 */
export interface PathWithTargets {
    path: string;
    targetScripts?: string[];
}

/**
 * Reads an optional Bxl JavaScript configuration file for a given project  and returns it
 * @param repoFolder The root of the repo
 * @param projectFolder The root of the project
 */
export function getBuildXLConfiguration(repoFolder: string, projectFolder: string) : BuildXLConfiguration {
    let pathToConfig = path.join(projectFolder, bxlConfigurationFilename);

    // This is an optional file, so if it is not there, just return an empty configuration
    if (!fs.existsSync(pathToConfig)) {
        return {outputDirectories: [], sourceFiles: []};
    }

    let configJson : Object = undefined;
    try {
        let configContent = fs.readFileSync(pathToConfig).toString();
        
        configJson = JSON.parse(configContent);
    }
    catch(error)
    {
        throw new Error(`An error was encountered trying to read BuildXL configuration file at '${pathToConfig}'. Details: ${error.message}`);
    }

    let outputDirectories = processPathsWithScripts(configJson["outputDirectories"], repoFolder, projectFolder);
    let sourceFiles = processPathsWithScripts(configJson["sourceFiles"], repoFolder, projectFolder);

    return {outputDirectories: outputDirectories, sourceFiles: sourceFiles};
}

function processPathsWithScripts(paths: (string | PathWithTargets)[], repoFolder: string, projectFolder: string) : PathWithTargets[] {
    let pathsWithTargets : PathWithTargets[] = [];

    if (!paths) {
        return [];
    }

    for (let path of paths)
    {
        if (typeof path === "string")
        {
            pathsWithTargets.push({path: processPath(repoFolder, projectFolder, path)})
        }
        else
        {
            pathsWithTargets.push({
                path: processPath(repoFolder, projectFolder, path.path), 
                targetScripts: path.targetScripts});
        }
    }

    return pathsWithTargets;
}

/** Resolves the path such that it is always an absolute path (or undefined) */
function processPath(workspaceFolder: string, projectFolder: string, aPath: string): string {
    if (!aPath) {
        return undefined;
    }

    if (aPath.indexOf(workspaceDirStartToken) == 0)
    {
        return path.join(workspaceFolder, aPath.substring(workspaceDirStartToken.length));
    }

    if (!path.isAbsolute(aPath)) {
        return path.join(projectFolder, aPath);
    }

    return aPath;
}