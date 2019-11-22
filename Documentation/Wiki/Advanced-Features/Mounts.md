# Mounts
Mounts are a way to specify filesystem scopes and rules for those scopes. They can be refered to by name in build specs as a way to abstract specification of common locations. Mounts are specified in the DScript configuration file for the build.

## Example mount definition

```typescript
config({
    mounts: [
        {
            // Unique name for the mount
            name: a`pictures`,

            // Path of the directory to mount (mount point)
            path: p`data/pictures`,

            // Mount is readable
            isReadable: true,

            // Mount is a system folder
            isSystem: false,

            // Mount is scrubbable. You can have "input" files that are not
            // registered in the current build graph, or "output" files that
            // BuildXL can delete.
            isScrubbable: true,

            // Mount is not writable
            isWriteable: false,

            // File changes are tracked for this mount by hashing file contents
            trackSourceFileChanges: true
        }
    ]
});
```

### Mount permissions
The `isReadable` and `isWritable` mount settings do not automatically give all pips access to read/write files without specifying those accesses. They are more like rule for what pips are allowed to specify. For example the `pictures` mount above has the `isReadable` property set to true. That means you may define a pip that reads from a path under this mount. However if a pip does not specify the read it makes under pictures, it will still get a Disallowed File Access at runtime.

### System Mounts
BuildXL has a few behavioral changes for system mounts. The primary one is that it may tokenize those paths. So if a system mount is at different locations on disk across different machines, they still may get cache hits if using the same shared cache.

## Define file system mounts
Mounts are symbolic names for a particular point in a file system, and associated configuration that controls how the file system is accessed.



## Default mounts

BuildXL ships with the following built-in mounts:

| Name              | Description                | Writable? | Scrubbable? |
| ----------------- | -------------------------- | --------- | ----------- |
| "BuildEnginePath" | layout.BuildXLBinDirectory | no        | no          |
| "SourceRoot"      | layout.SourceDirectory     | no        | no          |
| "ObjectRoot"      | layout.ObjectDirectory     | yes       | yes         |
| "TempRoot"        | layout.TempDirectory       | yes       | yes         |
| "LogsDirectory"   | layout.ObjectDirectory     | yes       | no          |


### System mounts

BuildXL includes a number of system mounts for specifying accesses that process pips make outside of source files. See [MountsTable.cs](../../../Public/Src/Engine/Dll/MountsTable.cs#L79) for a full accounting of default system mounts based on your platform.
