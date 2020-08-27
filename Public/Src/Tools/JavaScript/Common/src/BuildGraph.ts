import * as BxlConfig from './BuildXLConfigurationReader';

/**
 * A JavaScript graph, for what matters to BuildXL, is just a collection of projects
 */
export interface JavaScriptGraph {
    projects: JavaScriptProject[];
}

export interface ScriptCommands {
    [task: string] : string
}

/**
 * A JavaScript project, with the information that is relevant to BuildXL
 */
export interface JavaScriptProject {
    name: string;
    availableScriptCommands: ScriptCommands;
    projectFolder: string;
    tempFolder: string;
    dependencies: string[];
    outputDirectories: BxlConfig.PathWithTargets[];
    sourceFiles: BxlConfig.PathWithTargets[];
}