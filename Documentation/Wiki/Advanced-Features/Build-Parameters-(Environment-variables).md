# Environment variables
Like source code, environment variables are a form of input to a build. BuildXL tracks the environment variables visible to a build for sake of caching. As such, it is important to understand and control how environment variables are used in a build to allow for correct and efficient caching.

Consumption is divided into 2 categories:
1. Build logic
1. Child Processes (Pips)


## Passing variables to BuildXL
Environment variables can be set prior to launching bxl.exe or be specified on the command-line using the `/p` option:

`bxl.exe /p:x=1`

The `/p` option will override environment variables that may have already been specified from the context under which bxl.exe was launched.

## Build Logic
Users may want to base build logic on build parameters, for example to turn certain features on or off for the build. To do so, DScript allows access to environment variables through the [Environment functions](../../../Public/Sdk/Public/Prelude/Prelude.Environment.dsc). For example `Environment.getPathValue("BUILDXL_DEPLOY_ROOT")` in DScript will return a Path with the value of the BUILDXL_DEPLOY_ROOT environment variable. 

The main configuration file can access any environment variable. To limit consumption of environment variables in other build specification files, only variables explicitly set as allowed may be used outside of the main configuration file. This is controlled via [the configuration](..\..\..\Public\Sdk\Public\Prelude\Prelude.Configuration.dsc):

```ts
config({
    allowedEnvironmentVariables: ["x", "y"],
});
```

## Process Pips
When BuildXL launches a pip, the pip does not inherit all of the environment variables from the process that launches BuildXL. Instead, each process starts with a minimal environment of [base environment variables](#base-environment-variables). Build logic may append to or override these base variables. The value of those variables may be explicitly set in build logic or be marked as passthrough, meaning the current value will be passed through to the process but not included in fingerprints.

| Variable type | Caching Implications|  
|-----------|-----------:|  
| Base environment variables | Not tracked |
| DScript specified environment variables | Key and value tracked for caching | 
| DScript specified passthrough environment variables | Value is not tracked. Addition or removal is considered for caching |
| Global passthrough environment variables | Value is not tracked. Addition or removal is **not** considered for caching | 


## Passthrough environment variables
Environment variables can be marked as _passthrough_, meaning the environment variable value is not considered when fingerprinting the process. By default, the value is read from the environment of the bxl.exe process (which can be overridden by use of the [/p argument](#passing-variables-to-buildxl) ). However, the value of passthrough variables can also be explicitly set in DScript to something other than what is in bxl.exe's environment. The effect is the same in that the value will not be tracked.

```ts
    let result = Transformer.execute({
            tool: args.tool,
            description: args.description,
            // ...
            unsafe: {
                passThroughEnvironmentVariables: [
                    EnvVar1,
                    ...
                    EnvVarN
                ]
            }
    });
```

Passthrough variables may also be set globally for all processes in a build. The are set via a semicolon separate list passed on the command line with `/unsafe_GlobalPassthroughEnvVars`. The value of these variables may not be overridden.


### Base Environment Variables
| Variable | Value | OS | Note |
|--|--|--|--|
| NUMBER_OF_PROCESSORS | Passthrough | All | This allows the tool to parallelize as needed. BuildXL reserves the right in the future to tweak this number on the fly to maximize resource utilization on the machine. |
| PROCESSOR_ARCHITECTURE | Passthrough | All |
| PROCESSOR_IDENTIFIER | Passthrough | All |
| PROCESSOR_LEVEL | Passthrough | All|
| PROCESSOR_REVISION | Passthrough | All |
| OS| Passthrough | All |On supported Windows os's this is practically always `Windows_NT`
| SYSTEMDRIVE | Passthrough | All |
| SYSTEMROOT |Passthrough | All |
| SYSTEMTYPE |Passthrough | All |
| PATH | \$(WINDIR);$(SYSTEM)\wbem | Windows |  This is the minimal set of OS paths that tools typically need to function. | 
|  | /usr/bin;/usr/sbin;/bin;/sbin | Linux |   | 
| ComSpec |  "$(SYSDIR)\cmd.exe" | Windows | This is the standard shell for the tool |
| PATHEXT | ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC" | Windows | This is the standard search path when in a shell you write `x`, it wills search for `x.com`, `x.exe` etc.
| TMP | A non-writeable location | All | By default TMP and TEMP are set to a value that tools can't write to. When you create a pip via `Transformer.execute` you can specify that a tool needs a temporary folder and then this will be set to a tool-specific folder where it is allowed to read and write |
| TEMP | A non-writeable location | All | See TMP |
| TMPDIR | A non-writeable location | Linux | See TMP
                          
`$(SYSDIR)` in the table is defined as the result of [GetSystemDirectory()](https://msdn.microsoft.com/en-us/library/windows/desktop/ms724373(v=vs.85).aspx) 
`$(WINDOWS)` in the table is defined as the result of [SHGetFolderPath()](https://msdn.microsoft.com/en-us/library/windows/desktop/bb762181(v=vs.85).aspx) with [CSIDL_WINDOWS](https://msdn.microsoft.com/en-us/library/windows/desktop/bb762494(v=vs.85).aspx)

