| FileSystem | FullGraph | OutputGraph | IsDeclared | Mount | Access | EmptyDir | Result  
|:----------|:----------|:--------|:--------|:--------|:--------|:--------|:--------|
Absent | Absent | Absent | under SourceSealDirectory | Readable | Read/Probe | N/A | ObservedInput.AbsentFileProbe
Absent | Absent/ExistsAsFile | Absent/ExistsAsFile | No | Readable/Writable | Read/Probe | N/A | ObservedInput.AbsentFileProbe
Absent | ExistsAsDir | ExistsAsDir | N/A | Writable | Enumeration | N/A | ObservedInput.DirectoryEnumeration (via graph)
Absent | ExistsAsDir | ExistsAsDir | N/A | Writable | Probe | Yes/No | ObservedInput.ExistingDirectoryProbe
Absent | ExistsAsFile | Absent | Yes | Readable/Writable | Read/Probe | N/A | Failure in HashSourceFileDependencies
Absent | ExistsAsFile | Absent/ExistsAsFile | under SealDirectory | Readable/Writable | Read/Probe | N/A | ObservedInput.AbsentFileProbe
ExistsAsDir | Absent | Absent | N/A | Writable | Enumeration | Yes/No | ObservedInput.AbsentFileProbe
ExistsAsDir | Absent | Absent | N/A | Writable | Probe | Yes/No | ObservedInput.AbsentFileProbe
ExistsAsDir | Absent/ExistsAsDir | Absent/ExistsAsDir | N/A | Readable | Enumeration | No | ObservedInput.DirectoryEnumeration
ExistsAsDir | Absent/ExistsAsDir | Absent/ExistsAsDir | N/A | Readable | Enumeration | Yes | ObservedInput.AbsentFileProbe
ExistsAsDir | Absent/ExistsAsDir | Absent/ExistsAsDir | N/A | Readable | Probe | Yes/No | ObservedInput.ExistingDirectoryProbe
ExistsAsDir | Absent/ExistsAsDir | Absent/ExistsAsDir | N/A | Untracked | Enumeration | Yes/No | ObservedInput.AbsentFileProbe
ExistsAsDir | Absent/ExistsAsDir | Absent/ExistsAsDir | N/A | Untracked | Probe | Yes/No | ObservedInput.AbsentFileProbe
ExistsAsDir | ExistsAsDir | ExistsAsDir | N/A | Writable | Enumeration | Yes/No | ObservedInput.DirectoryEnumeration (via graph)
ExistsAsDir | ExistsAsDir | ExistsAsDir | N/A | Writable | Probe | Yes/No | ObservedInput.ExistingDirectoryProbe
ExistsAsFile | Absent | Absent | under SourceSealDirectory | Readable | Probe | N/A | ObservedInput.ExistingFileProbe
ExistsAsFile | Absent | Absent | under SourceSealDirectory | Readable | Read | N/A | ObservedInput.FileContentRead
ExistsAsFile | ExistsAsFile | Absent | Yes | Readable/Writable | Read/Probe | N/A | Part of WeakFingerprint
ExistsAsFile | ExistsAsFile | Absent/ExistsAsFile | under SealDirectory | Readable/Writable | Probe | N/A | ObservedInput.ExistingFileProbe
ExistsAsFile | ExistsAsFile | Absent/ExistsAsFile | under SealDirectory | Readable/Writable | Read | N/A | ObservedInput.FileContentRead
ExistsAsFile | ExistsAsFile | ExistsAsFile | No | Readable/Writable | Read/Probe | N/A | Unexpected access failure
