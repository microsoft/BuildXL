# File stream support

In the NTFS file system, streams are data (a sequence of byte) that is written to a file, and this data gives extra information about the file than attributes and properties. For details about streams, see [NTFS file streams](https://learn.microsoft.com/en-us/windows/win32/fileio/file-streams).

Currently, BuildXL does not support file stream accesses in the following sense: BuildXL can execute a process pips that accesses file streams, but
BuildXL may fail with access violations because streams are not representable in Detours' file access manifest, or will drop reports of
stream accesses altogether.

## Detours' logic for file stream accesses

Detours does not give a special treatment to default streams. However, because paths with streams are not representable in BuildXL's
internal path structure, such paths cannot be included into Detours' file access manifest, and thus, such accesses can result in access denied.
For example, an access to `file::$DATA` results in access denied.

On the other hand, Detours always allows accesses to files with non-default streams. For example, accesses to `file:test_stream:$Data` and `file:test_stream` are always allowed without checking the manifest.

## Pip executor's logic for file stream accesses

Even though Detours does not give a special treatment to the default stream, the pip executor will drop any reported file access with path
that cannot be parsed by BuildXL's internal path structure, including filenames with streams.

Moreover, the special streams are not going to be transited through the cache, and are not taken into account when hashing the file.

## Supporting file streams

BuildXL currently does not support file streams. To properly handle streams, there are a lot of changes needed, from the cache, our internal file
info (and file materialization info) structure, down to the Detours level.

In the near future, we may want to support default streams because, for example, accessing `file::$DATA` is the same as accessing `file`.
