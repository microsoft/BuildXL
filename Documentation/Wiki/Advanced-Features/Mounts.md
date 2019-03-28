> TODO: In Progress

* What mountpoints are
* What their purpose is
* Examples how to configure
* Mention projects should not willy-nilly refer to mount points, okay for SDKs

## Define file system mounts
Mounts are symbolic names for a particular point in a file system, and associated configuration that controls how the file system is accessed.

BuildXL supports directories as mount points, so you can mount a directory with a symbolic name and refer to that location using its name. You can control the characteristics of a mount to enable or disable certain file access operations or policies.

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

### Default mounts

BuildXL ships with the following built-in mounts:

| Name             | Description                | Writable? | Scrubbable? |
| ---------------- | -------------------------- | --------- | ----------- |
| "BuildXLBinPath" | layout.BuildXLBinDirectory | no        | no          |
| "SourceRoot"     | layout.SourceDirectory     | no        | no          |
| "ObjectRoot"     | layout.ObjectDirectory     | yes       | yes         |
| "TempRoot"       | layout.TempDirectory       | yes       | yes         |

// if layout.TempDirectory is valid

#### System mounts ####

Following names can be used to refer to system special folders:

| Name                    | System folder                        |
| ----------------------- | ------------------------------------ |
| "Windows"               | SpecialFolder.Windows                |
| "ProgramFiles"          | SpecialFolder.ProgramFiles           |
| "ProgramFilesX86"       | SpecialFolder.ProgramFilesX86        |
| "CommonProgramFiles"    | SpecialFolder.CommonProgramFiles     |
| "CommonProgramFilesX86" | SpecialFolder.CommonProgramFilesX86  |
| "InternetCache"         | SpecialFolder.InternetCache          |
| "InternetHistory"       | SpecialFolder.History                |
| "AppData"               | SpecialFolder.ApplicationData        |
| "LocalAppData"          | SpecialFolder.LocalApplicationData   |
| "LocalLow"              | FileUtilities.KnownFolderLocalLow    |
| "ProgramData"           | SpecialFolder.CommonApplicationData  |
| .UserProfileMountName   | SpecialFolder.UserProfile            |
