// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

/**
 * Repo configuration.
 */
@@public
export interface RepoConfig
{
    /** Repo root. */
    root: Directory;

    /** Repo environment variables. */
    environmentVariables?: Transformer.EnvironmentVariable[];
}

/**
 * Description of a project.
 */
@@public
export interface Project
{
    /** Root directory of the project. */
    root: Directory;

    /** Project name. */
    name: string;

    /** Platforms. */
    platforms?: string[];

    /** Build-time/compile-time dependencies. */
    buildtimeDeps?: Reference[];

    /** Runtime dependencies. */
    runtimeDeps?: Reference[];

    /** Runtime dev dependencies. */
    runtimeDevDeps?: Reference[];

    /** Project metadata. */
    metadata?: Object;

    /** Project specific environment variables. */
    environmentVariables?: Transformer.EnvironmentVariable[];
}

/**
 * Package reference.
 */
@@public
export interface PackageReference
{
    /** Dependency kind, e.g., NuGet, Npm, etc. */
    kind: string;

    /** Name. */
    name: string;

    /** Version. */
    version: string;

    /** Package directories. */
    directories: (Directory | StaticDirectory)[];
}

/**
 * NuGet package reference.
 */
@@public
export interface NuGetReference extends PackageReference
{
    /** NuGet marker. */
    kind: "NuGet";

    /** Package source for restoring the package. */
    source?: Directory;
}

/**
 * Project references.
 */
@@public
export type Reference = File | StaticDirectory | ProjectOutput | PackageReference;

/**
 * Outputs of running the workflow on a project.
 */
@@public
export interface ProjectOutput
{
    /**
     * Workflow outputs.
     */
    outputs: WorkflowOutput;

    /**
     * Project's immediate references.
     *
     * Needed for computing transitive closure of dependencies.
     */
    references?: Reference[];
}

/** Outputs of running a workflow. */
@@public
export type WorkflowOutput = (File | StaticDirectory | TaskOutput)[];

/** Outputs of running a task. */
@@public
export interface TaskOutput
{
    /** Task outputs. */
    taskOutputs: (File | StaticDirectory)[];

    /**
     * Task's immediate references.
     * 
     * Needed for computing transitive closure of task dependency.
     */
    taskReferences?: TaskOutput[];
}

/** Task dependency. */
@@public
export type TaskDependency = Reference | TaskOutput | Project;

function isProjectDependency(d: TaskDependency) : d is Project
{
    return typeof(d) === "object" && d["root"] !== undefined && d["name"] !== undefined;
}

function isProjectOutputDependency(d: TaskDependency) : d is ProjectOutput
{
    return typeof(d) === "object" && d["outputs"] !== undefined;
}

function isTaskOutputDependency(d: TaskDependency) : d is TaskOutput
{
    return typeof(d) === "object" && d["taskOutputs"] !== undefined;
}

function isPackageReferenceDependency(d: TaskDependency) : d is PackageReference
{
    return typeof(d) === "object" && d["kind"] !== undefined && d["name"] !== undefined;
}

function isFileOrStaticDirectoryDependency(d: TaskDependency) : d is File | StaticDirectory
{
    return typeof(d) !== "object";
}

/**
 * Flattens task dependencies into file/directory dependencies.
 */
export function flattenTaskDependencies(
    disableTransitiveProjectOutputDependency:     boolean,
    disableTransitiveTaskOutputDependency:        boolean,
    ...deps:                                      TaskDependency[]) : (File | StaticDirectory)[]
{
    // Filter out undefined dependencies.
    const definedDeps = deps.filter(d => d !== undefined);

    // Get dependencies on project outputs.
    const projectDeps = definedDeps
        .filter(d => isProjectDependency(d))
        .map(d => <Project>d)
        .mapMany(p => [...(p.buildtimeDeps || []), ...(p.runtimeDeps || []), ...(p.runtimeDevDeps || [])].filter(d => d !== undefined));
    let projectOutputs = [...definedDeps, ...projectDeps]
        .filter(d => isProjectOutputDependency(d))
        .map(d => <ProjectOutput>d);

    // Compute transitive closure of dependencies on project outputs if requested.
    if (!disableTransitiveProjectOutputDependency)
        projectOutputs = computeProjectOutputClosure(projectOutputs);

    // Collect task outputs from project outputs.
    const projectOutputDeps = projectOutputs.mapMany(p => p.outputs);
    const taskOutputsFromProjectOutputs = projectOutputDeps
        .filter(d => isTaskOutputDependency(d))
        .map(d => <TaskOutput>d);

    // Get dependencies on the task outputs.
    // Do no include the dependencies on the task outputs of the project outputs due to the project boundary
    let taskOutputs = definedDeps
        .filter(d => isTaskOutputDependency(d))
        .map(d => <TaskOutput>d);

    // Compute transitive closure of dependencies on task outputs if requested.
    if (!disableTransitiveTaskOutputDependency)
        taskOutputs = computeTaskOutputClosure(taskOutputs);

    const taskDeps = [...taskOutputs, ...taskOutputsFromProjectOutputs].mapMany(t => t.taskOutputs);
    const fileOrDirectoryDeps = [...definedDeps, ...projectOutputDeps]
        .filter(d => isFileOrStaticDirectoryDependency(d))
        .map(d => <File | StaticDirectory>d);

    // Get dependencies on package references, particularly the directories where the packages are installed/produced.
    const packageDeps = definedDeps
        .filter(d => isPackageReferenceDependency(d))
        .map(d => <PackageReference>d)
        .mapMany(p => p.directories)
        .map(d => typeof(d) === "Directory" 
            ? Transformer.sealSourceDirectory(d, Transformer.SealSourceDirectoryOption.allDirectories)
            : <StaticDirectory>d);

    return [
        ...packageDeps,
        ...taskDeps,
        ...fileOrDirectoryDeps
    ];
}

export function getProjectDependencyClosure(project: Project) : ProjectOutput[]
{
    const deps = [
        ...(project.buildtimeDeps || []),
        ...(project.runtimeDeps || []),
        ...(project.runtimeDevDeps || [])
    ].filter(d => d !== undefined);
    const referencedProjects: ProjectOutput[] = deps
        .filter(d => typeof(d) === "object" && d["outputs"] !== undefined)
        .map(d => <ProjectOutput>d);
    return computeProjectOutputClosure(referencedProjects);
}

function computeProjectOutputClosure(projectOutputs: ProjectOutput[]) : ProjectOutput[]
{
    let result = MutableSet.empty<ProjectOutput>();
    result.add(...projectOutputs);
    for (let p of projectOutputs) {
        computeProjectOutputClosureAux(p, result);
    }
    return result.toArray();
}

function computeProjectOutputClosureAux(projectOutput: ProjectOutput, result: MutableSet<ProjectOutput>) : void
{
    const referencedProjects: ProjectOutput[] = (projectOutput.references || [])
        .filter(r => typeof(r) === "object" && r["outputs"] !== undefined)
        .map(r => <ProjectOutput>r);
    for (let p of referencedProjects) {
        if (!result.contains(p)) {
            result.add(p);
            computeProjectOutputClosureAux(p, result);
        }
    }
}

function computeTaskOutputClosure(taskOutputs: TaskOutput[]) : TaskOutput[]
{
    let result = MutableSet.empty<TaskOutput>();
    result.add(...taskOutputs);
    for (let p of taskOutputs) {
        computeTaskOutputClosureAux(p, result);
    }
    return result.toArray();
}

function computeTaskOutputClosureAux(taskOutput: TaskOutput, result: MutableSet<TaskOutput>) : void
{
    const referencedTasks: TaskOutput[] = taskOutput.taskReferences || [];
    for (let t of referencedTasks) {
        if (!result.contains(t)) {
            result.add(t);
            computeTaskOutputClosureAux(t, result);
        }
    }
}

function getTaskOutputsFromDependencies(...deps: TaskDependency[]) : TaskOutput[]
{
    const definedDeps = deps.filter(d => d !== undefined);
    const taskOutputs = definedDeps
        .filter(d => typeof(d) === "object" && d["taskOutputs"] !== undefined)
        .map(d => <TaskOutput>d);
    return taskOutputs;
}

/**
 * Augments task outputs with references to other task outputs.
 */
export function augmentTaskOutputWithReferences(taskOutput: TaskOutput, refs?: TaskDependency[]) : TaskOutput
{
    const taskReferences = getTaskOutputsFromDependencies(...(refs || []));
    return taskOutput.merge({ taskOutputs: [], taskReferences: taskReferences });
}