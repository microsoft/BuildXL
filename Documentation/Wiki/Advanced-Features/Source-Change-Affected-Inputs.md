Source change affected input computation a feature of BuildXL that computes all the affected inputs of a process by the source change of the enlistment. BuildXL writes the full paths of the affected inputs of a process to a particular file indicated via the pip definition.

# Transitiveness
The source change impact propagates down the graph.

Suppose that we have the following pip dependency graph:
```
infileA -> (processA) -> outfileA -> (copyfile pip) -> outfileA-copy -> (processB) -> outfileB -> (processC) -> outfileC                                                  
```
When infileA was changed, the change affected input for processC is outfileB.

# Feature Adoption
The feature is currently used for changelist code coverage. BuildXL computes the source change affected input list for QTest pip. QTest will only process the file listed as affected when it computes code coverage results. This reduces the QTest's instrumentation time.

# Enabling The Feature
BuildXL needs to know the source changes for the computation. BuildXL will only perform the compution for the process that requires to know its affected inputs by providing the path of a file that the result can be written into.
## Source Change Tracking
Currently, BuildXL doesn't check the source change of the enlistment itself. It requires source change provided through the command line argument `/inputChanges:<path-to-file-containing-change-list>`. Full paths of the changed source files should be listed in this file.

Example of the file content:
```
D:\Git\MyRepo\src\fruit.cs
D:\Git\MyRepo\src\bar.cpp
```
## Providing a file path for writing the computation result
To enable the feature for a process, the spec author needs to provide a path to a file that the computation result will be written into. This file path is indicated by the `changeAffectedInputListWrittenFile` argument when calling `Transformer.execute()`. 

Example of Dscript code:

```ts
    Transformer.execute({ 
        tool: cmdExe, 
        description: "Process pip in ChangeAffectedInputTest", 
        arguments: [ 
            Cmd.argument("/d"), 
            Cmd.argument("/c"), 
            Cmd.argument(Artifact.input(f`./readFromAndWriteToFile.cmd`)), 
            Cmd.argument(Artifact.input(f`./affectedInputForLastProcess.txt`)) 
            Cmd.argument(Artifact.output(outfile3)), 
        ], 
        changeAffectedInputListWrittenFile : p`./file.txt`, 
        workingDirectory: d`.` 
    });
```
