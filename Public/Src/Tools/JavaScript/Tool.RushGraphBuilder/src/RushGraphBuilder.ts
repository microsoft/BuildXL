import * as path from 'path';
import * as semver from 'semver';
import * as BxlConfig from './BuildXLConfigurationReader';
import {JavaScriptGraph, JavaScriptProject} from './BuildGraph';

/**
 * A Rush graph, for what matters to BuildXL, is just a collection of projects and some graph-level configuration data
 */
export interface RushGraph extends JavaScriptGraph {
    configuration: RushConfiguration;
}

/**
 * Graph-level rush configuration with data that BuildXL cares about
 */
export interface RushConfiguration {
    commonTempFolder: string;
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
    let projects : JavaScriptProject[] = [];
    for (const project of rushConf.projects) {

        let dependencies = getDependencies(rushLib.Rush.version, rushConf, project);

        let bxlConfig : BxlConfig.BuildXLConfiguration = BxlConfig.getBuildXLConfiguration(rushConf.rushJsonFolder, project.projectFolder);

        let p: JavaScriptProject = {
            name: project.packageName,
            projectFolder: project.projectFolder,
            dependencies: dependencies,
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

function getDependencies(rushLibVersion: string, configuration, project) : string[] {
    // Starting from Rush version >= 5.30.0, there is built-in support to get the list of local referenced projects
    if (semver.gte(rushLibVersion, "5.30.0")) {
        return project.localDependencyProjects.map(rushConfProject => rushConfProject.packageName);
    }
    else {
        return Array.from(getDependenciesLegacy(configuration, project));
    }
}

function getDependenciesLegacy(configuration, project) : Set<string> {
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