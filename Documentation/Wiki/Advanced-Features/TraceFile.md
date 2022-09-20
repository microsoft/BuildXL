# File Access Trace File

Sometimes there is a need to analyze file access operations that were performed by pips.
While it can be done post build (by using [Execution Analyzer](Execution-Analyzer.md)), there are scenarios when such analysis must be done during the build.
File Access Trace File is a BuildXL feature that, when enabled, instructs the sandbox to record all observed file operations and save them to a file on disk.
The trace file is enabled on per pip basis, and each file contains accesses of the corresponding pip.
Other pips in a build can take dependency on that file and read it when they are executed.

To enable trace file, when a user specifies ExecuteArgument for Transformer.execute, they must provide a file path where they want the file to be saved.

```ts
const executeResult = Transformer.execute({
    ...
    fileAccessTraceFile: p`traceFile.txt`
});
```

Once the pip is executed, the trace file will be one of the pip's outputs and can be referenced like any other output in DScript.

## Format

The trace file is a text file with a simple format. 
Each logical unit is placed on a separate line.
The file is divided into seven blocks.

1. Version number<br />
An integer that represents current version.
    ```
    1
    ```

2. ReportedFileOperation block
    1. A count of observed file operation types.
    2. For each file operation, a mapping of its name to a byte value.
While there are many file operation types, the file will only contain those that were observed.

    ```
    7
    5=Process
    3=GetFileAttributes
    6=FindFirstFileEx
    29=NtCreateFile
    7=FindNextFile
    1=CreateFile
    4=GetFileAttributesEx
    ```

3. RequestedAccess block
    1. A count of unique levels of access. 
       While there are several basic levels of access, an operation might request any combination of them.
       For the purposes of this block, each unique combination is a separate level of access.
    2. For each level of access, a mapping of its id to a its string representation. 

    ```
    5
    1=Read
    4=Probe
    2=Write
    8=Enumerate
    16=EnumerationProbe
    ```

4. Process block
    1. A count of observed processes.
    2. Each process is represented by two lines. The first one contains a mapping from a process id to process details. The second one is command line arguments of a process. <br />
Process details: executable path, parent process id, UTC ticks of start and end time (note: sandbox does not report these values, instead timestamp of when an observation was received from sandbox is used here; therefore, these values are not true start/end time of a process), process exit code.
    ```
    1
    16592,"C:\hello.exe",29120,637989104119349710,637989104120394770,0
    C:\hello.exe XYZ
    ```

5. Manifest paths and Paths blocks
    1. A count of paths
    2. For each path, a mapping from a 64-bit integer to path’s string representation.

    ```
    15
    268435457=B:\
    268435458=B:\Out
    268435459=B:\Out\Objects
    ...
    ```

6. Operations block
    1. A count of observed operations
    2. For each operation: <br />
        1. A unique id (this id establishes the order of operations)
        2. Process ID
        3. ManifestPathId, PathId pair <br />
            Only one of these values is set. The id represents a path in a corresponding path block.
        4. FileOperationId - an id from ReportedFileOperation block
        5. RequestedAccess - an id from RequestedAccess block
        6. ErrorCode
        7. A flag indicating whether this is an augmented operation (0 - non-augmented, 1 - augmented)
        8. (optional) Enumeration pattern

    ```
    176
    0,16592,268435474,,5,1,0,0,
    1,16592,268435457,,3,4,0,0,
    2,16592,268435468,,3,4,0,0,
    ...
    73,16592,,35,29,1,0,0,
    ...
    134,16592,,76,6,8,0,0,*
    ```

## Performance / space considerations
To create a trace file, the sandbox requires data that is not usually collected. 
If a trace file is requested, the sandbox will operate as if the following arguments were set `/logObservedFileAccesses+`, `/logProcesses+`, `/logProcessData+`.
If a build is already running with these arguments, there is no performance penalty. 
If a build is not using them, enablement of this feature may cause a performance impact depending on the number of file operation performed and child processes launched by the pip.

Since observations are reported as is, i.e., no deduping, grouping, etc., the size of a produced trace file is not trivial. 
While it is difficult to estimate the increase in total output size (trace files are outputs of a build), one can use our test builds as guidance. 
A large-scale test build (440k pips, a trace file was for each pip) resulted in increase of total output size by 36GB. 


