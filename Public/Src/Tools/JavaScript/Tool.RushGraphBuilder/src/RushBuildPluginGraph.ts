/**
 * A typed version of what rush-build-graph-plugin returns in JSON format
 */
export interface RushBuildPluginGraph {
    nodes: RushBuildPluginNode[];
    repoSettings: {commonTempFolder: string};
}

/**
 * A node in the graph rush-build-graph-plugin returns
 */
export interface RushBuildPluginNode {
    id: string;
    task: string;
    package: string;
    dependencies: string[];
    workingDirectory: string;
    command: string;
}