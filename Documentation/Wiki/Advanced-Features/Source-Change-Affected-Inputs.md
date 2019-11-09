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

# Caching Behavior
When a process requires to know its source affected change inputs, BuildXL assumes that this process needs to use this information to do something. So it treats this computation result as an input of the process and counts it in the fingerprint. If the affected inputs of a process change, this process will get a cache miss. The process's cache behavior will be different from when the feature is disabled.

## Example
Suppose that we have the following pip dependency graph
```
infileA --> (processA) --> outfileA --> (processC) --> outfileC   
                                     /
infileB --> (processB) --> outfileB -                           
```

When processC disables the feature, only `outfileA` and `outfileB` are taken into account for processC's fingerprinting. But when processC enables the feature, its `affected inputs` is also taken into account for fingerprinting. The below table illustrates the fingerprint represents of processC in a sequence of builds. Build0 is a base build. Both infileA and infileB has no change in Build0. In Build3, infileB reverts the change in Build2 and back to version in Build1. When the feature is disabled, processC's fingerprint in Build3 is identical to the one in Build1. So processC gets a cache hit. However, when the feature is enabled, its fingerprint in Build3 can't find any match in the previous builds. So it gets a cache miss.

|         | infileA Content | infileB Content| Affected Inputs of ProcessC |Fingerprint of ProcessC (Feature Enabled) | Fingerprint of ProcessC (Feature Disabled) | 
|---------|:---------------:|:--------------:|:---------------------------:|:----------------------------------------:|:------------------------------------------:|
| Build0  | a               |  b             |  None                       |a+b               |  a+b                  |
| Build1  | a'              |  b             |  outfileA                   |a'+b+outfileA     |  a'+b                 |
| Build2  | a'              |  b'            |  outfileB                   |a'+b'+outfileB    |  a'+b'                |
| Build3  | a'              |  b             |  outfileB                   |a'+b+outfileB     |  a'+b                 |


