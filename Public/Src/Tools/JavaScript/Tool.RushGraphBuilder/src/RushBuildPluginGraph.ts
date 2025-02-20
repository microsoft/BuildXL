// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    // Cacheable is an optional field since not all rush versions have it
    // When absent, the default value is true
    cacheable?: boolean;
}