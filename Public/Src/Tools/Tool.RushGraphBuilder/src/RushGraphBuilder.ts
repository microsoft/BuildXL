import { RushConfiguration, RushConfigurationProject } from '@microsoft/rush-lib';
import * as semver from 'semver';
import * as path from 'path';
import * as fs from 'fs';
import { FileSystem, JsonSchema, JsonFile } from '@microsoft/node-core-library';
import { IJestTaskConfiguration } from './IJestTaskConfiguration';

const JEST_TASK_CONFIGURATION_SCHEMA: JsonSchema = JsonSchema.fromFile(
    path.join(__dirname, 'schemas', 'jestTaskConfiguration.schema.json')
  );

/**
 * A Rush graph, for what matters to BuildXL, is just a collection of projects
 */
export interface RushGraph {
    projects: RushProject[];
}

/**
 * A strip down version of a Rush project, with the information that is relevant to BuildXL
 */
export interface RushProject {
    name: string;
    buildCommand: string;
    projectFolder: string;
    tempFolder: string;
    dependencies: string[];
    additionalOutputDirectories: string[];
}

/**
 * Builds a RushGraph from a valid rush configuration file
 */
export function buildGraph(rushConfigurationFile: string, isDebug: boolean): RushGraph
{
    // TODO: consider making the node-core library parametric
    // Load Rush configuration, which includes the build graph
    let rushConf = RushConfiguration.loadFromConfigurationFile(rushConfigurationFile);

    // Map each rush project into a RushProject node
    let projects : RushProject[] = [];
    for (const project of rushConf.projects) {

        let buildCommand = undefined;

        if (project.packageJson.scripts && project.packageJson.scripts['build']) {
            // TODO: this should go away and we should take the script as is
            buildCommand = project.packageJson.scripts['build']
                .replace('gulp test', 'gulp')
                .replace('gulp.js test', 'gulp.js');
        
            // We should be able to extract these arguments from the Rush conf object
            buildCommand += ' --production';
            buildCommand += ' --verbose';

            if (isDebug) {
                buildCommand += ' --locale qps-ploc';
            }
        }
    
        let dependencies = getDependencies(rushConf, project);

        let p: RushProject = {
            name: project.packageName,
            projectFolder: project.projectFolder,
            dependencies: Array.from(dependencies),
            buildCommand: buildCommand,
            tempFolder: project.projectRushTempFolder,
            additionalOutputDirectories: getAdditionalOutputDirectories(project)
            };

        projects.push(p);
        
        // If Jest is enabled, add a node representing a test execution
        if (project.packageJson.scripts && project.packageJson.scripts['build'] && isJestEnabled(project)) {
            let testCommand = project.packageJson.scripts['build']
                .replace('gulp test', 'gulp test-only')
                .replace('gulp.js test', 'gulp.js test-only')
                .replace(' --clean', ''); // Need to remove this for tests to actually run

            testCommand += ' --production';

            let testDependencies = dependencies.add(project.packageName);
            let test: RushProject = {
                name: project.packageName + "_test",
                projectFolder: project.projectFolder,
                dependencies: Array.from(testDependencies),
                buildCommand: testCommand,
                tempFolder: project.projectRushTempFolder,
                additionalOutputDirectories: getAdditionalOutputDirectories(project)
                };

            projects.push(test);
        }
    }

    return {projects: projects};
}

function getDependencies(configuration: RushConfiguration, project: RushConfigurationProject) : Set<string> {
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
function getDependency(name: string, version: string, project: RushConfigurationProject, configuration: RushConfiguration) : string {
    if (
        !project.cyclicDependencyProjects.has(name) &&
        configuration.projectsByName.has(name)
      ) {
        const dependencyProject: RushConfigurationProject = configuration.projectsByName.get(name)!;
        if (semver.satisfies(dependencyProject.packageJson.version, version)) {
          return dependencyProject.packageName;
        }
      }
  
      return undefined;
}

/**
 * Use 'directories' in package.json to allow projects to specify additional output directories (additional to
 * the project root, which is a default output directory)
 */
function getAdditionalOutputDirectories(project: RushConfigurationProject) : Array<string>{
    
    // 'directories' doesn't seem to be exposed from rush-lib. Parse package.json manually and extract the field
    // if present
    let packagejson : string = fs.readFileSync(path.join(project.projectFolder, "package.json")).toString();
    let json = JSON.parse(packagejson);
    let directories : Map<string, string> = json["directories"] as Map<string, string>;
    
    if (directories) {
        // We don't care about the symbolic names used for each directory, just take the paths
        let result : string[] = [];
        for (const directoryName in directories) {
            let directory = directories[directoryName];
            result.push(path.join(project.projectFolder, directory));
        }
        return result;
    }

    return undefined;
}

function isJestEnabled(project: RushConfigurationProject): boolean {
    const jestTaskConfigurationPath: string = path.resolve(project.projectFolder, 'config', 'jest.json');
    const jestTaskConfiguration: Partial<IJestTaskConfiguration> = FileSystem.exists(jestTaskConfigurationPath)
      ? JsonFile.loadAndValidate(jestTaskConfigurationPath, JEST_TASK_CONFIGURATION_SCHEMA)
      : { isEnabled: false };

    return jestTaskConfiguration.isEnabled ? true : false;
  }