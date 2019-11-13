# BuildXL demos
Some demos are included under /Public/Src/Demos to showcase the main features of the BuildXL sandbox. These demos are not intended to be a comprehensive walkthrough, but just a simple set of examples that can be used as a starting point to learn how the sandbox can be configured and used.

The BuildXL sandbox is capable of running an arbitrary process and 'detour' all its OS calls. As a result, it is possible to obtain very rich information about what the process is doing and even block some of the process calls. In order to do this, the sandbox needs to be configured before a process is run. This configuration will instruct the sandbox about what to monitor and block.

The sandbox is configured through a *manifest*. The manifest contains high-level information (for example, if child processes should be monitored or not) but also more fine-grained information related to how to treat the running process file accesses. As part of the manifest, *scopes* can be defined (i.e. a directory and all its recursive content), where each scope can have its own policy. A policy includes what operations are allowed for a given scope (e.g. the process can be allowed to read and write under c:\foo, but only read under c:\foo\readonly. A policy also includes if file accesses should be reported back, and if a file access is expected or not. Blocking an access is also a capability that can be specified via a policy.

These demos showcase three main features of the sandbox: reporting file accesses, blocking accesses and retrieving the process tree.

## Using the sandbox to report file accesses (Public/Src/Demos/ReportAccesses)

This demo is able to run an arbitrary process and report back all the file accesses that the process (and its child processes) made. For example, one can run:

```
E:\temp>dotnet <repo_root>\bin\Debug\ReportAccesses.dll notepad myFile.txt
```

This will actually open notepad.exe and myFile.txt will be created. After exiting notepad, the tool reports:

```
Process 'notepad' ran under BuildXL sandbox with arguments 'myFile.txt' and returned with exit code '0'. Sandbox reports 27 file accesses:
C:\WINDOWS\SYSTEM32\notepad.exe
C:\Windows\Fonts\staticcache.dat
C:\WINDOWS\Registration\R00000000000d.clb
C:\WINDOWS\Registration
C:\WINDOWS\Registration\R000000000001.clb
C:\WINDOWS\Globalization\Sorting\sortdefault.nls
C:\WINDOWS\SYSTEM32\OLEACCRC.DLL
E:\temp
E:\temp\myFile.txt
C:\WINDOWS\SYSTEM32\imageres.dll
C:\WINDOWS\SysWOW64\propsys.dll
C:\WINDOWS\system32\propsys.dll
C:\ProgramData\Microsoft\Windows\Caches
C:\ProgramData\Microsoft\Windows\Caches\cversions.2.db
C:\Users\username\AppData\Local\Microsoft\Windows\Caches
C:\Users\username\AppData\Local\Microsoft\Windows\Caches\cversions.3.db
C:\ProgramData\Microsoft\Windows\Caches\{DDF571F2-BE98-426D-8288-1A9A39C3FDA2}.2.ver0x0000000000000000.db
```

Let's take a closer look at the code to understand how this happened. A sandbox is configured via `SandboxedProcessInfo`. The main information to be provided here is 1) what process to run 2) the arguments to be passed and 3) the manifest to be used to configure the sandbox:

```cs
// Public/Src/Demos/ReportAccesses/FileAccessReporter.cs
var info =
    new SandboxedProcessInfo(
        PathTable,
        new SimpleSandboxedProcessFileStorage(workingDirectory), 
        pathToProcess,
        CreateManifestToAllowAllAccesses(PathTable),
        disableConHostSharing: true,
        loggingContext: m_loggingContext)
    {
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        PipSemiStableHash = 0,
        PipDescription = "Simple sandbox demo",
        SandboxConnection = OperatingSystemHelper.IsUnixOS ? new SandboxConnectionKext() : null
    };
``` 

`CreateManifestToAllowAllAccesses` is where the most interesting things happen:

```cs
var fileAccessManifest = new FileAccessManifest(pathTable)
    {
        FailUnexpectedFileAccesses = false,
        ReportFileAccesses = true,
        MonitorChildProcesses = true,
    };
```            
We are creating a manifest that configures the sandbox so:
* No file accesses are blocked (`FailUnexpectedFileAccesses = false`)
* All files accesses are reported (`ReportFileAccesses = true`)
* Child processes are also monitored (`MonitorChildProcesses = true`)

As a result of this configuration, all file accesses are allowed and reported. Each file access carries structured information that includes the type of operation, disposition, attributes, etc. In this simple demo we are just printing out the path of each access.

This demo works on mac as well, but only supports absolute paths in the arguments.

```
~/BuildXL$ dotnet <repo_root>/out/bin/Demos/Debug/netcoreapp2.2/ReportAccesses.dll /bin/echo
Process '/bin/echo' ran under BuildXL sandbox with arguments '' and returned with exit code '0'. Sandbox reports 48 file accesses:
/bin/echo
/usr/lib/dyld
/private/var/db/dyld/dyld_shared_cache_x86_64h
/usr/lib/libSystem.B.dylib
/usr/lib/system/libcache.dylib
/usr/lib/system/libcommonCrypto.dylib
/usr/lib/system/libcompiler_rt.dylib
/usr/lib/system/libcopyfile.dylib
...
...
...
...
/usr/lib/system/libsystem_notify.dylib
/usr/lib/system/libsystem_sandbox.dylib
/dev/dtracehelper
/usr/lib/system/libsystem_secinit.dylib
/usr/lib/system/libsystem_kernel.dylib
/usr/lib/system/libsystem_platform.dylib
/AppleInternal
/usr/lib/system/libsystem_pthread.dylib
/usr/lib/system/libsystem_symptoms.dylib
/usr/lib/system/libsystem_trace.dylib
/usr/lib/system/libunwind.dylib
/usr/lib/system/libxpc.dylib
/usr/lib/libobjc.A.dylib
/usr/lib/libc++abi.dylib
/usr/lib/libc++.1.dylib
```

## Blocking accesses (Public/Src/Demos/BlockAccesses)

The next demo shows how to use BuildXL sandbox to actually block accesses with certain characteristics. Given a directory provided by the user, a process is launched under the sandbox which tries to enumerate the given directory recursively and perform a read on every file found. However, a collection of directories to block can also be provided: the sandbox will make sure that any access that falls under these directories will be blocked, preventing the tool from accessing those files.

Consider the following directory structure:

```
E:\TEST
├───bin
│       t1.exe
│
├───obj
│       t1.obj
│
└───source
        t1.txt
```        

And let's see what happens if we run:

```
dotnet <repo_root>\bin\Debug\BlockAccesses.dll e:\test e:\test\bin e:\test\obj
```

Here we are trying to enumerate ``e:\test`` recursively, but block any access under ``e:\test\obj`` and ``e:\test\bin``. The result is:

```
Enumerated the directory 'e:\test'. The following accesses were reported:
Allowed -> [Read] C:\WINDOWS\system32\cmd.exe
Allowed -> [Probe] e:\test
Allowed -> [Probe] e:
Allowed -> [Enumerate] e:\test
Allowed -> [Enumerate] e:\test\..
Allowed -> [Enumerate] e:\test\bin
Allowed -> [Enumerate] e:\test\obj
Allowed -> [Enumerate] e:\test\source
Allowed -> [Enumerate] e:\test\bin\..
Allowed -> [Enumerate] e:\test\bin\t1.exe
Allowed -> [Probe] e:\test\bin
Denied -> [Probe] e:\test\bin\t1.exe
Allowed -> [Enumerate] e:\test\obj\..
Allowed -> [Enumerate] e:\test\obj\src2.txt
Allowed -> [Enumerate] e:\test\obj\t1.obj
Allowed -> [Probe] e:\test\obj
Denied -> [Probe] e:\test\obj\t1.obj
Allowed -> [Enumerate] e:\test\source\.
Allowed -> [Enumerate] e:\test\source\t1.txt
Allowed -> [Probe] e:\test\source
Allowed -> [Probe] e:\test\source\t1.txt
Allowed -> [Read] e:\test\source\t1.txt
```

Each access is reported, and for each case, we are printing out the type of access: a read, a check for existence (a probe) or an enumeration. Additionally, if the access was allowed or denied. Observe that since we are only blocking accesses under obj and bin, only these two accesses were blocked:

```
Denied -> [Probe] e:\test\obj\t1.obj
Denied -> [Probe] e:\test\bin\t1.exe
```
And given that the probe was blocked, there was not even an attempt to read from those files, since they failed at enumeration time to begin with.

Let's jump now into more details to understand how this was achieved. If we look at how the manifest was constructed, we can see the following:

```cs 
// Public/Src/Demos/BlockAccesses/BlockingEnumerator.cs

// We allow all file accesses at the root level, so by default everything is allowed
fileAccessManifest.AddScope(AbsolutePath.Invalid, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowAll);

// We block access on all provided directories
foreach (var directoryToBlock in directoriesToBlock)
{
    fileAccessManifest.AddScope(
        directoryToBlock,
        FileAccessPolicy.MaskAll,
        FileAccessPolicy.Deny & FileAccessPolicy.ReportAccess);
}
```

The first line is setting the policy for the *global* (or root) scope. You can think of that as the default policy for an arbitrary access. This policy is just allowing any access to happen. Then, we iterate over the directories that have to be blocked, and for each one, we add a *scope*. A scope can be thought of as a directory with all its recursive content. Scopes can be nested, and the most specific scope is the one that defines the policy for an access that falls under it.

In this case, we are configuring each scope so all accesses are blocked (```FileAccessPolicy.Deny```) but also to report the access (```FileAccessPolicy.ReportAccess```). Additionally, we have to decide what to do with the policy we are inheriting from the parent scope. Here one can decide to mask some of the parent configuration. In this case, we are just masking all the properties from the parent, to make sure nothing is allowed.

Finally, we are retrieving all the accesses by inspecting the result of the sandbox:

```cs
// Public/Src/Demos/BlockAccesses/Program.cs
SandboxedProcessResult result = sandboxDemo.EnumerateWithBlockedDirectories(directoryToEnumerate, directoriesToBlock).GetAwaiter().GetResult();

var allAccesses = result
    .FileAccesses
    .Select(access => $"{(access.Status == FileAccessStatus.Denied ? "Denied" : "Allowed")} -> {RequestedAccessToString(access.RequestedAccess)} {access.GetPath(pathTable)}")
    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
```

``SandboxedProcessResult.FileAccesses`` contains all the reported accesses. So we just iterate over them and print some of the details.

This demo works on mac as well (with the same directory structure as before)

```
~$ dotnet <repo root>/bin/tests/Demos/Debug/BlockAccesses.dll ~/test/ ~/test/obj/ ~/test/bin/
Enumerated the directory '/Users/BuildXLUser/test/'. The following accesses were reported:
Allowed -> [Read] /usr/bin/find
Allowed -> [Read] /usr/lib/dyld
Allowed -> [Probe] /usr/bin/find
Allowed -> [Probe] /private/var/db/dyld/dyld_shared_cache_x86_64h
Allowed -> [Probe] /usr/lib/libSystem.B.dylib
...
...
...
...
Allowed -> [Probe] /usr/lib/libc++.1.dylib
Allowed -> [Probe] /AppleInternal/XBS/.isChrooted
Allowed -> [Read] find
Allowed -> [Read] /bin/cat
Allowed -> [Probe] /bin/cat
Allowed -> /bin/cat
Allowed -> /usr/bin/find
Allowed -> [Probe] /Users/BuildXLUser/test
Allowed -> [Enumerate] /Users/BuildXLUser/test
Allowed -> [Enumerate] /Users/BuildXLUser/test/obj
Allowed -> [Probe] /Users/BuildXLUser/test/obj
Allowed -> [Probe] /Users/BuildXLUser/test/bin
Allowed -> [Probe] /Users/BuildXLUser/test/source
Denied -> [Read] /Users/BuildXLUser/test/obj/t1.obj
Allowed -> [Enumerate] /Users/BuildXLUser/test/bin
Denied -> [Read] /Users/BuildXLUser/test/obj/src2.txt
Denied -> [Read] /Users/BuildXLUser/test/bin/t1
Allowed -> [Enumerate] /Users/BuildXLUser/test/source
Allowed -> [Read] /Users/BuildXLUser/test/source/t1.txt
```

## Retrieving the process list
The last demo shows how the sandbox can be used to retrieve the list of processes spawned by a process that was run under the sandbox. All child processes that are created during the execution of the main process is reported, together with structured information that contains IO and CPU counters, elapsed times, etc.

For example, let's run a git fetch on an arbitrary repo:

```
dotnet <repo_root>\bin\Debug\ReportProcesses.dll git fetch
```

The result is:

```
Process 'git' ran under the sandbox. These processes were launched in the sandbox:
C:\Program Files\Git\cmd\git.exe [ran 675.7914ms]
C:\Program Files\Git\mingw64\bin\git.exe [ran 608.794ms]
C:\Program Files\Git\mingw64\libexec\git-core\git.exe [ran 528.7287ms]
C:\Program Files\Git\mingw64\libexec\git-core\git-remote-https.exe [ran 488.156ms]
C:\Program Files\Git\mingw64\libexec\git-core\git.exe [ran 35.8792ms]
C:\Program Files\Git\mingw64\libexec\git-core\git.exe [ran 37.1245ms]
C:\Program Files\Git\mingw64\libexec\git-core\git.exe [ran 30.3581ms]
```

The demo is printing out the process list, including the elapsed running time for each process.

Let's jump into the code. The manifest creation for this demo is not super interesting, the only relevant part being setting a specific flag to log the data of all spawned processes:

```cs
// Public/Src/Demos/ProcessTree/ProcessReporter.cs
 var fileAccessManifest = new FileAccessManifest(pathTable)
{
    ...
    // Monitor children processes spawned
    MonitorChildProcesses = true,
};
```

The list of processes are reported as part of the sandbox result:

```cs
// Public/Src/Demos/ReportProcesses/ProcessReporter.cs
SandboxedProcessResult result = RunProcessUnderSandbox(pathToProcess, arguments);
// The sandbox reports all processes as a list.
return result.Processes;
```
All the processes (main and children) are reported in ``SandboxedProcessResult.Processes`` as a list of processes. Here, we decided to print the path of the process executable, and the running time:

```cs
/// Public/Src/Demos/ReportProcesses/Program.cs
Console.WriteLine($"{reportedProcess.Path} [ran {(reportedProcess.ExitTime - reportedProcess.CreationTime).TotalMilliseconds}ms]");
```

Here is the process list reported on Mac

```
~/BuildXL$ dotnet out/bin/Demos/Debug/netcoreapp2.2/ReportProcesses.dll /usr/bin/git fetch
Process '/usr/bin/git' ran under the sandbox. These processes were launched in the sandbox:
/usr/bin/git [ran 0ms]
/Applications/Xcode.app/Contents/Developer/usr/libexec/git-core/git-remote-http [ran 0ms]
/Applications/Xcode.app/Contents/Developer/usr/libexec/git-core/git [ran 0ms]
/Applications/Xcode.app/Contents/Developer/usr/libexec/git-core/git [ran 0ms]
/Applications/Xcode.app/Contents/Developer/usr/libexec/git-core/git [ran 0ms]
/Applications/Xcode.app/Contents/Developer/usr/libexec/git-core/git [ran 0ms]
/Applications/Xcode.app/Contents/Developer/usr/libexec/git-core/git [ran 0.001ms]
```