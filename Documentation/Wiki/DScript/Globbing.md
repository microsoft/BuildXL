# Globbing

As the list of build inputs grows, entering each file by hand (e.g., to specify all projects that belong to a module , or a list file containing all imports can become tiresome.  To that end, the DScript SDK provides built-in functions for adding files and directories by name: `glob`, `globR`, `globRecursively`, and `globFolders`.

## Function definitions

All glob functions take 2 arguments: a root directory of the search, and a search pattern.  The search pattern supports one wildcard character, the asterisk (`*`), which matches all characters.  The search pattern is optional, and when omitted defaults to `"*"`.  The `glob` function searches for files that match the search pattern and reside exactly in the specified root directory; to search for files recursively, use `globR` or `globRecursively` (which are synonyms); finally, to search for folders, use `globFolders`.

The full glob API is given below.

```ts
/** 
 * Glob files; if pattern is undefined, then the default is "*". 
 * Returns the empty array if the globed directory does not exist.
 */
declare function glob(folder: Directory, pattern?: string): File[];

/** 
 * Glob folders; if pattern is undefined, then the default is "*".
 * Returns the empty array if the globed directory does not exist. 
 */
declare function globFolders(folder: Directory, pattern?: string): Directory[];

/** 
 * Glob files recursively; if pattern is undefined, then the default is "*" 
 * Returns the empty array if the globed directory does not exist.
 */
declare function globR(folder: Directory, pattern?: string): File[];
declare function globRecursively(folder: Directory, pattern?: string): File[];
```

## Examples

```ts
// Get all C# files in 'myDirectory'
const x : File[] = glob(d`myDirectory`, '*.cs');

// Get all TXT files in 'myOtherDirectory'
const y : File[] = globR(d`myOtherDirectory`, '*.txt');

// This is equivalent to the above (globR is effectively an alias for globRecursively)
const y2 : File[] = globRecursively(d`myOtherDirectory`, '*.txt');

// Get the names of all folders in the current directory
const z : Directory[] = globFolders(d`.`);
```

## One level wildcards
The glob function supports one special wildcard pattern. If the pattern starts with `*/` or `*\` then files will match only one directory level under the specified directory.
For example if you run: 
```ts
globSubDirectories(d`.`, "a.txt") 
```
with the following directory structure:
```txt 
  \
  │   a.txt
  ├───F1
  │       a.txt
  │
  ├───F2
  │       b.txt
  │
  └───F3
      │   a.txt
      │   b.txt
      │
      └───F4
              a.txt
``` 
It will return:
 *  `f1/a.txt`
 *  `f3/a.txt`

It will not match any files in the passed folder. i.e. it will not match /a.txt
It will also not recurse after one level of folders, i.e. it will not match /f3/f4/a.txt

## Best practices

The performance of `glob` and its variants can suffer when used in _extremely_ large codebases.  In such cases, using explicit lists may turn out to be the only feasible option.

Using very permissive patterns like `"*"` or `"*.*"` is generally not recommended.  Such patterns can easily pick up unintended files (e.g., temporary files of whose existence on disk the user might not even be aware of), which is likely to lead to hard-to-debug errors.  For example, some text editors automatically create backup files next to the originals; when that is the case, specifying `"*.*"` instead of `"*.cpp"` can easily lead to spurious "duplicate identifier" compilation errors.  Specifying a wildcard with a known extension is usually a safe option.

To prevent any such incidents, globbing can be completely disabled by specifying the `"NoGlob"` policy in the main configuration file (`config.bc`), as shown below:

```ts
config({
    frontEnd: {
        enabledPolicyRules: [ "NoGlob" ]
    }
});
```

When the `"NoGlob"` policy is specified, for every appearance of a glob function in any build file, BuildXL emits the following error:

`error DX9100: [NoGlob] Globbing is not allowed.`
