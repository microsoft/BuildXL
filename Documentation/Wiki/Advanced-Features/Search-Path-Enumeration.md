# Search Path Enumeration
BuildXL tracks directory enumerations made by the processes it invokes during the build and generates a fingerprint based on the file names.

Currently, this fingerprint includes all members of the directory as determined by the file system. This causes a cache miss whenever a file is added or removed to the directory. Often, tools do not care about all files in the directory. Namely, if the directory is a search path, the tool only cares about files with a given file name (ignoring extension). For instance, cmd.exe would search for files of the form (some_command.exe, some_command.cmd, some_command.bat) and other tools might not care about the extension at all. To handle this case, the build can be configured to declare certain tools, based on executable path, as using search path enumeration and all directory enumerations coming from that tool will be treated as search paths and using search path directory membership fingerprinting. We ignore the extension, when considering if a file name should be included in the directory membership fingerprint for a search path to account for the fact that tools may not care about the extension or may care about a set of extensions. By ignoring the extension, we err on the side of correctness and handle both cases.

## Search Path Enumeration Observed Input Criteria
In order for an observed directory enumeration input to be recognized as a search path enumeration:

* All enumerations to the directory by processes the process tree launched by BuildXL for the pip must be from processes configured as search path enumeration tools.

## Behavior
For search path directory enumerations, we will only record files in the directory which match the filename, without directory and extension, of some observed input in the set of search paths. To account for tools like robocopy, there will be include/exclude list for tools to indicate that whether their directory enumerations should take into account the full set of files in the directory. This list applies to the specific process with the given directory enumerations, not just the root process launched by BuildXL.

The set of file names which would be included in the directory membership fingerprints (`includedFileNameSet` below), would be the file/directory names of members under the search path for **all** observed accesses and **all** declared dependencies.

For a process with the following observations, assuming robocopy has an ALL_FILES directory membership fingerprint rule and all others use SEARCH_PATH enumeration:

##### Observed Enumerations:

```
Dir1\
Dir2\
Dir3\
Dir4\ [from robocopy]
Dir5\
Dir5\Dir6\
```

##### Declared Dependencies
```
Dir2\E.cpp
```

##### Undeclared Observed File Accesses/Probes:
```
Dir1\A.h
Dir2\B.exe
Dir3\C.h
Dir1\Dir7\D.exe
```

The fingerprints will be computed as follows:
```
var includedFileNameSet = ['Dir7', ‘Dir6’, ‘A’, ‘B’, ‘C’, ‘E’]
Hash(GetFilesWithFileNamesInDirectory(‘Dir1’, includedFilesNameSet))
Hash(GetFilesWithFileNamesInDirectory(‘Dir2’, includedFilesNameSet))
Hash(GetFilesWithFileNamesInDirectory(‘Dir3’, includedFilesNameSet))
Hash(GetFilesWithFileNamesInDirectory(‘Dir5’, includedFilesNameSet))
Hash(GetFilesWithFileNamesInDirectory(‘Dir5\Dir6’, includedFilesNameSet))
Hash(GetAllFileNames (‘Dir4’))
```
In the example above, the set of search paths are `['Dir1', 'Dir2', 'Dir3', 'Dir5', 'Dir5\Dir6\']`. Only `'Dir4'` using full enumeration since it comes from robocopy.
 
* `'Dir7'` is included in `includedFileNameSet` because of the access to `'Dir1\Dir7\D.exe'`. `'Dir7'` is the member inside the search path `'Dir1'` so it gets included rather than `'D'`.
* `'Dir6'` is included in `includedFileNameSet` because of the access to `'Dir5\Dir6\'`. `'Dir6'` is the member inside the search path `'Dir5'`.
* `'A'` is included in `includedFileNameSet` because of the access to `'Dir1\A.h'`. `'Dir1'` is a search path.
* `'B'` is included in `includedFileNameSet` because of the access to `'Dir2\B.exe'`. `'Dir2'` is a search path.
* `'C'` is included in `includedFileNameSet` because of the access to `'Dir3\C.h'`. `'Dir3'` is a search path.
* `'E'` is included in `includedFileNameSet` because of the declared dependency `'Dir2\E.cpp'`. `'Dir12'` is a search path. NOTE: `'E'` will appear even if `'Dir2\E.cpp'` was not accessed since it is a declared dependency.

* `'D'` is NOT included in `includedFileNameSet` because `'Dir1\Dir7'` is not a search path.

## Language support & API
Tools that utilize search path enumerations are specified in the config file. Tools are specified as relative paths where all components of the relative path must match. So in the example below, lib.exe tools located outside of a parent "customTools" directory would not get search path enumeration treatment.

DScript:
```ts
config({
    // ...
    searchPathEnumerationTools: [
        r`cl.exe`,
        r`\customTools\lib.exe`
    ],
});
```
