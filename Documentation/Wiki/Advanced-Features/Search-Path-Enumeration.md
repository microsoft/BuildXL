# Search Path Enumeration
BuildXL tracks directory enumerations made by the processes it invokes during the build and generates a fingerprint based on the file names.

Currently, this fingerprint includes all members of the directory as determined by the file system. This causes a cache miss whenever a file is added or removed to the directory. Often, some tools do not care about all files in the directory when they perform directory enumerations. For example, tools like cl.exe,
enumerate a directory to find and read a particular header file because the compiled program has `#include<file.h>`. In other words, that the tool enumerates can be
thought of as a search path. Suppose that cl.exe enumerates directories `Dir1` and `Dir2` to find `file.h` due to `#include<file.h>`. If we simply take all file names in `Dir1` and `Dir2` into their directory fingerprints, then any addition or removal of header files in those directories will invalidate the fingerprints, and the pip needs to rebuild, although the added/removed header files are not used by the program.

Some tools may only care about files with a given file name, possibly ignoring their extensions. For instance, when invoking some_command, cmd.exe would search for files of the form some_command.exe, some_command.cmd, or some_command.bat; other tools might not care about the extension at all. 

The build can be configured to declare certain tools, based on their executable paths, as using so-called search path enumeration. In this configuration, all directory enumerations coming from those tools will be treated as search paths and their membership fingerprints are calculated by including only file names of files that are read by the tools ignoring the file extensions. We ignore the extension to account for the fact that tools may not care about the extension or may care about a set of
extensions. By ignoring the extension, we err on the side of correctness and handle both cases.

To account for tools like robocopy, there is an include/exclude list for tools to indicate whether their directory enumerations should take into account the full set of files in the directory. This list applies to the specific process with the given directory enumerations, not just the root process of the pip launched by BuildXL.

## Search path enumeration criteria

For an observed directory enumeration to be recognized as a search path enumeration, all enumerations of that directory by processes in the process tree of the pip must be from processes that are configured as search path enumeration tools. That is, if a directory `Dir` is enumerated by tools `T1` and `T2` during a pip execution, and only `T1` is configured as search path enumeration tool, then `Dir` cannot be treated as a search path.

## Example of search path enumeration behavior

Consider a pip that has the following (static) declared dependency:
```
Dir2\E.cpp
```
Suppose that the build is configured so that only robocopy is using all-files directory membership fingerprinting rule, while other tools are using search path enumeration.

After execution, here are the observed directory enumerations that the pip performs:
```
Dir1\
Dir2\
Dir3\
Dir4\ [coming from robocopy]
Dir5\
Dir5\Dir6\
```
The set of search paths are `Dir1`, `Dir2`, `Dir3`, `Dir5`, and `Dir5\Dir6`. Only `Dir4` is using full enumeration since it comes from robocopy.

The pip also performs reads/probes on the following files that are not declared statically as dependencies:
```
Dir1\A.h
Dir2\B.exe
Dir3\C.h
Dir1\Dir7\D.exe
```

The directory fingerprints are shown in fhe following pseudocode:
```ts
var includedFilesNameSet = ["Dir7", "Dir6", "A", "B", "C", "E"]
var fpOfDir1 = Fingerprint(GetFilesWithFileNamesInDirectory("Dir1", includedFilesNameSet))
var fpOfDir2 = Fingerprint(GetFilesWithFileNamesInDirectory("Dir2", includedFilesNameSet))
var fpOfDir3 = Fingerprint(GetFilesWithFileNamesInDirectory("Dir3", includedFilesNameSet))
var fpOfDir5 = Fingerprint(GetFilesWithFileNamesInDirectory("Dir5", includedFilesNameSet))
var fpOfDir6 = Fingerprint(GetFilesWithFileNamesInDirectory("Dir5\\Dir6", includedFilesNameSet))
var fpOfDir4 = Fingerprint(GetAllFileNamesInDirectory("Dir4"))
```

The set of file names included in the directory membership fingerprints (`includedFileNameSet`) are the file/directory names of members under the search path for **all** observed accesses and **all** declared dependencies.

`GetFilesWithFileNamesInDirectory("Dir", fileNameSet)` returns all file names (without extensions) in `Dir` that are contained in `fileNameSet`.
`GetAllFileNamesInDirectory("Dir")` returns all file names in `Dir`.

In the above example, we have the following results:
* `Dir7` is included in `includedFileNameSet` because of the access to `Dir1\Dir7\D.exe`. `Dir7` is a member inside the search path `Dir1` so it gets included rather than `D`.
* `Dir6` is included in `includedFileNameSet` because of the access to `Dir5\Dir6`. `Dir6` is the member inside the search path `Dir5`.
* `A` is included in `includedFileNameSet` because of the access to `Dir1\A.h`. `Dir1` is a search path.
* `B` is included in `includedFileNameSet` because of the access to `Dir2\B.exe`. `Dir2` is a search path.
* `C` is included in `includedFileNameSet` because of the access to `Dir3\C.h`. `Dir3` is a search path.
* `E` is included in `includedFileNameSet` because of the declared dependency `Dir2\E.cpp`. `Dir12` is a search path. Note `E` will appear even if `Dir2\E.cpp` was not accessed since it is a declared dependency.
* `D` is **not** included in `includedFileNameSet` because `Dir1\Dir7` is not a search path.

## Language support & API
Tools that utilize search path enumerations are specified in the config file. Tools are specified as relative paths where all components of the relative path must match. So in the example below, lib.exe tools located outside of a parent `customTools` directory would not get search path enumeration treatment.

```ts
// DScript config.
config({
    // ...
    searchPathEnumerationTools: [
        r`cl.exe`,
        r`\customTools\lib.exe`
    ],
});
```
