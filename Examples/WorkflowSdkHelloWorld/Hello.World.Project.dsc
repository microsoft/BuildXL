import * as Workflow from "Sdk.Workflow";

// Constructs a script line to copy one path to another
function copyCommand(from: string, to: string) {
    return Context.getCurrentHost().os === "win" ? `COPY ${from} ${to}` : `cp ${from} ${to}`;
}

// Defines the tasks to run
function tasks() : Workflow.WorkflowOutput {
    const taskHello = Workflow.runScriptTask({
        script:           ["echo Hello > hello.txt"],
        workingDirectory: d`${Context.getMount("Out").path}`
    });
    
    // The output path can be specified with an environment variable

    const hwFile = Environment.getStringValue("HELLOWORLD_OUT") || "HelloWorld.txt";
    const copyCmd = copyCommand("hello.txt", hwFile);
    Debug.writeLine(copyCmd);
    const taskWorld = Workflow.runScriptTask({
        script:       [copyCmd, `echo World >> ${hwFile}`],
        workingDirectory: d`${Context.getMount("Out").path}`,
        dependencies: [taskHello]
    });

    // Just returning the leaf taskWorld - taskHello will get executed too because it is a dependency 
    return [taskWorld];
}

// The entry points for a build are all top-level constants from all modules included in config.dsc,
// so this declaration will make the build execute the tasks
const result = tasks();