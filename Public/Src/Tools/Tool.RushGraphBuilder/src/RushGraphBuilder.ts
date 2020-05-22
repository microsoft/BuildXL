import * as path from 'path';
import * as semver from 'semver';
import * as BxlConfig from './BuildXLConfigurationReader';

/**
 * A Rush graph, for what matters to BuildXL, is just a collection of projects and some graph-level configuration data
 */
export interface RushGraph {
    projects: RushProject[];
    configuration: RushConfiguration;
}

/**
 * Graph-level rush configuration with data that BuildXL cares about
 */
export interface RushConfiguration {
    commonTempFolder: string;
}

/**
 * A strip down version of a Rush project, with the information that is relevant to BuildXL
 */
export interface RushProject {
    name: string;
    availableScriptCommands: any;
    projectFolder: string;
    tempFolder: string;
    dependencies: string[];
    outputDirectories: BxlConfig.PathWithTargets[];
    sourceFiles: BxlConfig.PathWithTargets[];
}

/**
 * Builds a RushGraph from a valid rush configuration file
 */
export function buildGraph(rushConfigurationFile: string, pathToRushLib:string): RushGraph
{
    let rushLib;
    try {
        rushLib = require(path.join(pathToRushLib, "@microsoft/rush-lib"));
    }
    catch(error) {
        throw new Error(`Cannot find @microsoft/rush-lib module under '${pathToRushLib}'. This module is required to compute the Rush project graph. Details: ${error}`);
    }

    // Load Rush configuration, which includes the build graph
    let rushConf = rushLib.RushConfiguration.loadFromConfigurationFile(rushConfigurationFile);
        // Map each rush project into a RushProject node
    let projects : RushProject[] = [];
    for (const project of rushConf.projects) {

        let dependencies = getDependencies(rushConf, project);

        let bxlConfig : BxlConfig.BuildXLConfiguration = BxlConfig.getBuildXLConfiguration(rushConf.rushJsonFolder, project.projectFolder);

        let p: RushProject = {
            name: project.packageName,
            projectFolder: project.projectFolder,
            dependencies: Array.from(dependencies),
            availableScriptCommands: project.packageJson.scripts,
            tempFolder: project.projectRushTempFolder,
            outputDirectories: bxlConfig.outputDirectories,
            sourceFiles: bxlConfig.sourceFiles
            };

        projects.push(p);
    }

    return {
        projects: projects, 
        configuration: {commonTempFolder: rushConf.commonTempFolder}
    };
}

function getDependencies(configuration, project) : Set<string> {
    let dependencies: Set<string> = new Set<string>();
    
    // Collect all dependencies and dev dependencies 
    for (const dependencyName in project.packageJson.dependencies) {
        let version = project.packageJson.dependencies[dependencyName];
        
        let dependency = getDependency(dependencyName, version, project, configuration);
        if (dependency) {
            dependencies.add(dependency);
        }
    }

    for (const devDependencyName in project.packageJson.devDependencies) {
        let version = project.packageJson.devDependencies[devDependencyName];
        let dependency = getDependency(devDependencyName, version, project, configuration);
        if (dependency) {
            dependencies.add(dependency);
        }
    }

    return dependencies;
}

/**
 * Gets a dependency from a give rush project only if it is not a cyclic one and semver is satisfied
 */
function getDependency(name: string, version: string, project, configuration) : string {
    if (
        !project.cyclicDependencyProjects.has(name) &&
        configuration.projectsByName.has(name)
      ) {
        const dependencyProject = configuration.projectsByName.get(name)!;
        if (semver.satisfies(dependencyProject.packageJson.version, version)) {
          return dependencyProject.packageName;
        }
      }
  
      return undefined;
}