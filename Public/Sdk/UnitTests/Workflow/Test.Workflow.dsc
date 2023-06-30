import {Transformer, Cmd, Artifact} from "Sdk.Transformers";
import {Assert, Testing} from "Sdk.Testing";
import * as Workflow from "Sdk.Workflow";

namespace Sdk.Tests {

    @@Testing.unitTest()
    export function runSimpleTask(){
        Testing.setMountPoint({ name: a`RepoRoot`, path: p`.`, trackSourceFileChanges: true, isWritable: true, isReadable: true, isScrubbable: true });
        const task = Workflow.runTask({
            executable:       f`src/foo.exe`,
            arguments:        ["--opt", "bar", "--out", p`out/out.txt`],
            workingDirectory: d`src`
        });

        // No output is specified -- default to use RepoRoot mount path.
        Assert.isTrue(pathInTaskOutputs(p`.`, task));
    }

    @@Testing.unitTest()
    export function runInlineScriptTask(){
        Testing.setBuildParameter("COMSPEC", d`src/cmd.exe`.toDiagnosticString());
        const task = Workflow.runScriptTask({
            script:           ["CALL foo.exe --opt bar --out out.txt"],
            workingDirectory: d`src`
        });

        // No output is specified -- default to use the working directory as output when RepoRoot mount is unspecified.
        Assert.isTrue(pathInTaskOutputs(p`src`, task));
    }

    @@Testing.unitTest()
    export function runFileScriptTask(){
        Testing.setMountPoint({ name: a`RepoRoot`, path: p`.`, trackSourceFileChanges: true, isWritable: true, isReadable: true, isScrubbable: true });
        Testing.setBuildParameter("COMSPEC", d`src/cmd.exe`.toDiagnosticString());
        const task = Workflow.runScriptTask({
            scriptFile:       f`foo.cmd`,
            arguments:        ["--opt", "bar", "--out", p`out/out.txt`],
            workingDirectory: d`src`
        });

        // No output is specified -- default to use RepoRoot mount path.
        Assert.isTrue(pathInTaskOutputs(p`.`, task));
    }

    @@Testing.unitTest()
    export function runTransformerTask(){
        const task = Workflow.runTransformerTask({
            tool:                       { exe: f`src/foo.exe` },
            arguments:                  [Cmd.option("--opt ", "bar"), Cmd.option("--out ", Artifact.output(p`out/out.txt`))],
            workingDirectory:           d`src`,
            dependencies:               [f`src/input.txt`],
            outputs:                    [{kind: "shared", directory: d`out/OutDir`}],
            tempDirectory:              Context.getTempDirectory("foo"),
            allowUndeclaredSourceReads: true
        });

        // No output is specified -- default to use the working directory as output when RepoRoot mount is unspecified.
        Assert.isTrue(pathInTaskOutputs(p`out/outDir`, task));
    }

    @@Testing.unitTest()
    export function runWorkflowTasks(){
        // taskA <-- taskB <-- taskD
        //   ^                   |
        //   |                   |
        //   +------ taskC <-----+
        const taskA = Workflow.runTask({
            executable:       f`src/fooA.exe`,
            arguments:        ["--opt", "barA", "--out", p`out/outA/outA.txt`],
            workingDirectory: d`src`,
            outputs:          [d`out/outA`]
        });
        const taskB = Workflow.runTask({
            executable:       f`src/fooB.exe`,
            arguments:        ["--opt", "barB", "--out", p`out/outB/outB.txt`],
            workingDirectory: d`src`,
            dependencies:     [taskA],
            outputs:          [d`out/outB`]
        });
        const taskC = Workflow.runTask({
            executable:       f`src/fooC.exe`,
            arguments:        ["--opt", "barC", "--out", p`out/outC/outC.txt`],
            workingDirectory: d`src`,
            dependencies:     [taskA],
            outputs:          [d`out/outC`]
        });
        const taskD = Workflow.runTask({
            executable:       f`src/fooD.exe`,
            arguments:        ["--opt", "barD", "--out", p`out/outD/outD.txt`],
            workingDirectory: d`src`,
            dependencies:     [taskB, taskC],
            outputs:          [d`out/outD`]
        });

        // Ensure that taskD has a dependency on the outputs of taskA, taskB, and taskC.
        Assert.isTrue(taskInTaskReferences(taskA, taskB));
        Assert.isTrue(taskInTaskReferences(taskA, taskC));
        Assert.isTrue(taskInTaskReferences(taskB, taskD));
        Assert.isTrue(taskInTaskReferences(taskC, taskD));
        Assert.isTrue(taskInTaskReferences(taskA, taskD));
    }

    @@Testing.unitTest()
    export function runWorkflowTasksNonTransitive(){
        // taskA <-- taskB <-- taskD
        //   ^                   |
        //   |                   |
        //   +------ taskC <-----+
        const taskA = Workflow.runTask({
            executable:       f`src/fooA.exe`,
            arguments:        ["--opt", "barA", "--out", p`out/outA/outA.txt`],
            workingDirectory: d`src`,
            outputs:          [d`out/outA`]
        });
        const taskB = Workflow.runTask({
            executable:       f`src/fooB.exe`,
            arguments:        ["--opt", "barB", "--out", p`out/outB/outB.txt`],
            workingDirectory: d`src`,
            dependencies:     [taskA],
            outputs:          [d`out/outB`]
        });
        const taskC = Workflow.runTask({
            executable:       f`src/fooC.exe`,
            arguments:        ["--opt", "barC", "--out", p`out/outC/outC.txt`],
            workingDirectory: d`src`,
            dependencies:     [taskA],
            outputs:          [d`out/outC`]
        });
        const taskD = Workflow.runTask({
            executable:                             f`src/fooD.exe`,
            arguments:                              ["--opt", "barD", "--out", p`out/outD/outD.txt`],
            workingDirectory:                       d`src`,
            dependencies:                           [taskB, taskC],
            outputs:                                [d`out/outD`],
            disableTransitiveTaskOutputDependency:  true
        });

        // Ensure that taskD only has a dependency on the outputs of taskB and taskC.
        Assert.isTrue(taskInTaskReferences(taskA, taskB));
        Assert.isTrue(taskInTaskReferences(taskA, taskC));
        Assert.isTrue(taskInTaskReferences(taskB, taskD));
        Assert.isTrue(taskInTaskReferences(taskC, taskD));

        // Although in the lkg taskD is shown to not depend on taskA, but taskA will still
        // be included in the task references of taskD, only that it will not be used as a dependency
        // when creating the process pip. Currently, there is no way to assert dependencies of the pips
        // created by a task. So, the best way right now to manually inspect the corresponding .lkg.
        Assert.isTrue(taskInTaskReferences(taskA, taskD));
    }

    @@Testing.unitTest()
    export function runWorkflowProjects(){
        // projectP <-- projectQ

        const repoConfig: Workflow.RepoConfig = { root: d`src` };
        const projectP = runWorkflow(repoConfig, {
            root: d`src/P`,
            name: "P"
        });

        Assert.isTrue(pathInProjectOutput(p`out/P/outB`, projectP));
        Assert.isFalse(pathInProjectOutput(p`out/P/outA`, projectP));

        const projectQ = runWorkflow(repoConfig, {
            root:           d`src/Q`,
            name:           "Q",
            runtimeDeps:    [projectP]
        });

        Assert.isTrue(pathInProjectOutput(p`out/Q/outB`, projectQ));
        Assert.isFalse(pathInProjectOutput(p`out/Q/outA`, projectQ));

        // Ensure in the .lkg that taskB of Q include dependencies on the outputs of taskA of Q and the outputs of taskB of P, but not
        // the outputs of taskA of P due to the project boundary.
    }

    function runWorkflow(repoConfig: Workflow.RepoConfig, project: Workflow.Project) : Workflow.ProjectOutput
    {
        return Workflow.run(repoConfig, project, workflow);
    }

    function workflow(repoConfig: Workflow.RepoConfig, project: Workflow.Project) : Workflow.WorkflowOutput
    {
        // taskA <-- taskB
        const taskA = Workflow.runTask({
            executable:       f`src/fooA.exe`,
            arguments:        ["--opt", "barA", "--out", p`out/${project.name}/outA/outA.txt`],
            workingDirectory: d`src/${project.name}`,
            outputs:          [d`out/${project.name}/outA`]
        });
        const taskB = Workflow.runTask({
            executable:       f`src/fooB.exe`,
            arguments:        ["--opt", "barB", "--out", p`out/${project.name}/outB/outB.txt`],
            workingDirectory: d`src/${project.name}`,
            dependencies:     [taskA, project], // depends on previous task A, and also the dependencies specified by the project.
            outputs:          [d`out/${project.name}/outB`]
        });

        return [taskB];
    }

    /** Checks if a path is in an array of outputs of files or static directories. */
    function pathInOutputs(path: Path, outputs: (File | StaticDirectory)[]) : boolean
    {
        const paths = outputs.map(o => typeof(o) === "StaticDirectory" ? (<StaticDirectory>o).root.path : (<File>o).path);
        return paths.some(p => p === path);
    }

    /** Checks if a path is in one of the specified task outputs. */
    function pathInTaskOutputs(path: Path, ...tasks: Workflow.TaskOutput[]) : boolean
    {
        return pathInOutputs(path, tasks.mapMany(t => t.taskOutputs));
    }

    /** Checks if a path is in project output. */
    function pathInProjectOutput(path: Path, project: Workflow.ProjectOutput) : boolean
    {
        const taskOutputs = project.outputs.filter(t => t["taskOutputs"] !== undefined).map(t => <Workflow.TaskOutput>t);
        return pathInTaskOutputs(path, ...taskOutputs);
    }

    /** Checks if a task is in one of the specified task outputs. */
    function taskInTaskOutputs(task: Workflow.TaskOutput, ...tasks: Workflow.TaskOutput[]) : boolean
    {
        return tasks.some(t => t === task);
    }

    /** Checks if a path is in one of the specified task references. */
    function pathInTaskReferences(path: Path, task: Workflow.TaskOutput) : boolean
    {
        const allReferences = computeTaskOutputClosure(task.taskReferences);
        return pathInTaskOutputs(path, ...allReferences);
    }

    /** Checks if a task, task1, is in the references of another task, task2. */
    function taskInTaskReferences(task1: Workflow.TaskOutput, task2: Workflow.TaskOutput)
    {
        const allReferences = computeTaskOutputClosure(task2.taskReferences);
        return taskInTaskOutputs(task1, ...allReferences);
    }

    function computeTaskOutputClosure(taskOutputs: Workflow.TaskOutput[]) : Workflow.TaskOutput[]
    {
        let result = MutableSet.empty<Workflow.TaskOutput>();
        result.add(...taskOutputs);
        for (let p of taskOutputs) {
            computeTaskOutputClosureAux(p, result);
        }
        return result.toArray();
    }

    function computeTaskOutputClosureAux(taskOutput: Workflow.TaskOutput, result: MutableSet<Workflow.TaskOutput>)
    {
        const referencedTasks: Workflow.TaskOutput[] = taskOutput.taskReferences || [];
        for (let t of referencedTasks) {
            if (!result.contains(t)) {
                result.add(t);
                computeTaskOutputClosureAux(t, result);
            }
        }
    }
}
