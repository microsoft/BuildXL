# Environment variables
Lots of build systems allow environment variables to leak into the build specifications and the tools they run and allow them to be modified during execution.
[Other build Systems](#Other-build-systems) have reliability and reproducibility issues because of their lax allowances with regard to environment variables.

## BuildXL pips
When BuildXL launches a pip it does not inherit any of the environment variables from the process that launches BuildXL. Each process starts in a pristine empty environment. Each OS requires a minimal of environment variables for tools to run properly. On [Windows](#windows-fixed-environment-variables) we have a small set of fixed environment variables we have to set for tools to operate successfully.
All other environment variables for the pip have to be explicitly declared in the build specs. The user declared environment variables all are part of the fingerprint, so changing any environment variable will cause the tool to rerun properly.

## DScript
Sometimes users want to base their build logic on build parameters, for example to turn certain features on or off for the entire build. To do so, DScript allows users access to certain environment variables using the getPathValue function via the `Environment` namepace, e.g. `Environment.getPathValue("BUILDXL_DEPLOY_ROOT")`. The main configuration file can access any environment variable. Projects are only allowed to access environment variables that the config explicity allows via 

```ts
config({
    allowedEnvironmentVariables: ["x", "y"],
});
```

## Command-Line
Each environment variable can be specified on the command-line using the `/p` option:

`bxl.exe /p:x=1`

The /p option will override environment variables that may have already been specified from the context under which bxl.exe was launched.

## Specifying environment variables to be passed to Process pips
In BuildXL each tool that runs in the engine starts with a basic environment variable set. Basically the minimum needed to run a process on Windows (see list below). Any additional state needs to be specified when the pip is added to the build graph.

```ts
    let result = Transformer.execute({
            tool: args.tool,
            description: args.description,
            // ...
            environmentVariables:[ {}] ,

});
```

Environment variables can be marked as Passthrough, meaning the environment variable value is not considered when fingerprinting the process.

### Windows fixed environment variables.
| Variable | Value | Note |
|--|--|--|
| NUMBER_OF_PROCESSORS | Passthrough | This allows the tool to parallelize as needed. BuildXL reserves the right in the future to tweak this number on the fly to maximize resource utilization on the machine. |
| PROCESSOR_ARCHITECTURE | Passthrough | 
| PROCESSOR_IDENTIFIER | Passthrough | 
| PROCESSOR_LEVEL | Passthrough | 
| PROCESSOR_REVISION | Passthrough | 
| OS| Passthrough | On supported Windows os's this is practically always `Windows_NT`
| SYSTEMDRIVE | Passthrough |
| SYSTEMROOT |Passthrough |
| SYSTEMTYPE |Passthrough |
| ComSpec |  "$(SYSDIR)\cmd.exe" | This is the standard shell for the tool |
| PATH | "$(SYSDIR) ; $(Windows) ; $(SYSDIR)\wbem" | This is the minimal set of OS paths that tools typically need to function |
| PATHEXT | ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC" | This is the standard search path when in a shell you write `x`, it wills earch for `x.com`, `x.exe` etc.
| TMP | A non-writeable location | By default TMP and TEMP are set to a value that tools can't write to. When you create a pip via `Transformer.execute` you can specify that a tool needs a temporary folder and then this will be set to a tool-specific folder where it is allowed to read and write |
| TEMP | A non-writeable location | See TMP |
                          
`$(SYSDIR)` in the table is defined as the result of [GetSystemDirectory()](https://msdn.microsoft.com/en-us/library/windows/desktop/ms724373(v=vs.85).aspx) 
`$(WINDOWS)` in the table is defined as the result of [SHGetFolderPath()](https://msdn.microsoft.com/en-us/library/windows/desktop/bb762181(v=vs.85).aspx) with [CSIDL_WINDOWS](https://msdn.microsoft.com/en-us/library/windows/desktop/bb762494(v=vs.85).aspx)


# Other build systems
For example in MSBuild you can write:
```xml
<PropertyGroup>
   <MyValue>$(APPDATA)\someFolder</MyValue>
</PropertyGroup>

<Target>
  <SetEnvironmentVariableTask Name="Path" Value="$(MyValue);$(Path)" />
  <Exec Command="echo %PATH%"/>
</Target>
```
After the `SetEnvironmentVariableTask`, MSBuild can do things in parallel and the order of target execution is not guaranteed. Thus, it is not deterministic which tools will run with which PATH environment variable. Not to mention the problems of differences between users.

NMake let's you can do similar things:
```
MyValue=$(APPDATA)\someFolder 
PATH= $(MyValue);$(PATH)

all:  
    echo %PATH%  
```

As it has the same behavior as MSBuild, this has similar problems. Although in NMake targets will run in the same deterministic order (unless they take non-deterministic input).