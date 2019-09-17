# Generic ProtoBuf details and Implementation

This directory contains all of the proto files that represent the ojects stored in XLDB.
For some guidelines and information regarding ProtoBuf, check out these two links: [ProtoBuf 3 Guide](https://developers.google.com/protocol-buffers/docs/proto3), and [ProtoBuf Style Guide](https://developers.google.com/protocol-buffers/docs/style).

We store serialized ProtoBuf objects as the keys and values in RocksDB, and due to our use of PrefixSearch to query stored data, there are some restrictions on what keys can contain (there are NO restictions for values). 

### Restrictions:
1. The elements in the Key must be in order from most general to most specific. This way the prefix search can grab the general information first and then you can scope in as needed with more specific values.

2. There cannot be nested messages within the Key (primitives only). The reason is ProtoBuf will _prepend_ bytes with information related to nested message lengths when serializing, and if these lengths differ between different keys, the prefix search will not work as intended.

An important thing to keep in mind when changing any proto file is that the order of messages matters heavily to keep things forward and backwards compatible. Be careful when adding new fields and deprecating old ones! Furthermore default values (int = 0, bool = false, string = "", message=null, etc) are not serialized on the wire to save space on the wire, and the string representation of these Protobuf objects will not deserialize the default values either (though the instantiated class WILL have the values). 

* Do not be alarmed if the value is missing, it means its either unassigned or it is the default value.
* To force these values to be printed out in C#, you can `JToken.Parse(JsonConvert.SerializeObject(proto_obj, Formatting.Indented))`

<br>

# ProtoBuf Files:

## Events.Proto

The Events.proto file contains all of the protobuf schemas for the events (i.e., Things parsed from the .xlg file).

The key is defined by the message `EventKey` and the values are the different event messages.

Event Key and Values:
``` ProtoBuf
// Get the DB events (that are emitted during the build) with this key
message EventKey{ ... }

/// Possible values are:

// The FileArtifactContentDecided Event message
message FileArtifactContentDecidedEvent{ ... }

// The WorkerList Event message
message WorkerListEvent{ ... }

// The PipExecutionPerformance Event message
message PipExecutionPerformanceEvent{ ... }

// The DirectoryMembershipHashed Event message
message DirectoryMembershipHashedEvent{ ... }

// The ProcessExecutionMonitoringReported Event message
message ProcessExecutionMonitoringReportedEvent{ ... }

// The BuildSessionConfiguration Event message. 
message BuildSessionConfigurationEvent{ ... }

// The DependencyViolationReported Event message
message DependencyViolationReportedEvent{ ... }

// The PipExecutionStepPerformanceReported Event message
message PipExecutionStepPerformanceReportedEvent{ ... }

// The StatusReported Event message
message StatusReportedEvent{ ... }

// The ProcessFingerprintComputation Event message
message ProcessFingerprintComputationEvent{ ... }

// The PipCacheMiss Event message
message PipCacheMissEvent{ ... }

// The PipExecutionDirectoryOutputs Event message
message PipExecutionDirectoryOutputsEvent{ ... }

// The BxlInvocation Event message
message BxlInvocationEvent{ ... }
```

We also store some statistics about the DB such as how many events of a particular type were encountered and what the raw, uncompressed size of the ProtoBuf equivalent objects are. 

The key and value for that is as follows:

``` ProtoBuf
// Key for the DBStorageStats by the Protobuf object type
message DBStorageStatsKey{ ... }

// Value for the DB Storage Stats
message DBStorageStatsValue{ ... }
```

## StaticGraph.proto

The StaticGraph.proto file contains metadata about the static graph as well as the keys for pips. 
We have a special indexing style for pips in that we can search by SemistableHash or by PipId. 
To do this, we associate a `SemistableHashKey -> PipIdKey`.
We can then use this PipIdKey to get the actual pip value from the DB. 
Alternatively, if you already have the PipId, you can create the PipIdKey and get the pip value in 1 step.
Both ways are exposed to the user in the API.

Keys and Values for Pips:
``` ProtoBuf
// Pip Query is 2 levels deep. SemistableHash -> PipId -> PipValues
// For the pips with SemiStableHash = 0, we will only have PipId -> Values
message PipSemistableHashKey{ ... }

// Pip id Key 
message PipIdKey{ ... }

// Possible values are:

// ProcessPip message
message ProcessPip{ ... }

// WriteFile pip message
message WriteFile{ ... }

// CopyFile pip message
message CopyFile{ ... }

// SealDirectory pip message. This pip is a scheduler-internal pip representing the completion of a directory.
// Once this pip's inputs are satisfied, the underlying directory (or the specified partial view) is immutable.
message SealDirectory{ ... }

// IpcPip message. Specification of a Process invocation via an IPC message
message IpcPip{ ... }
```

We store some metadata for the Pip Graph as well as all the mount information in this file.

``` ProtoBuf
// GraphMetadataKey is the key for the top level data structures stored in the cached graph such as the CachedGraph, or the MountPathExpander
message GraphMetadataKey{ ... }

// Values:
// Pip Graph metadata message
message PipGraph{ ... }

// MountPathExpander data message
message MountPathExpander{ ... }
```

Finally, this file also contains messages related to Producer/Consumer indexing that we created where we can quickly find all producers or consumers for a given File or Directory path.

``` ProtoBuf
// Generic Key to get either a producer or a consumer of a file
message FileProducerConsumerKey{ ... }

// Generic Key to get either a producer or a consumer of a directory 
message DirectoryProducerConsumerKey{ ... }

// Values:

// Value for the producer of a file
message FileProducerValue{ ... }

// Value for consumers of a file
message FileConsumerValue{ ... }

// Value for the producer of a directory
message DirectoryProducerValue{ ... }

// Value for the consumers of a directory
message DirectoryConsumerValue{ ... }
```

## HelperStructs.proto

The HelperStructs.proto file contains a bunch of Bxl structs -> ProtoBuf messages that are used by the Events.Proto and StaticGraph.proto files.
These messages include things like AbsolutePath, FileArtifact, DirectoryArtifact, and more.

## Enums/*.proto

This directory contains all of the enums have been ported over from the BXL codebase.
Each enum is approximately shifted by 1 (either + 1 or << 1) relative the BXL enums since ProtoBuf guidelines require the 0 value for each enum to be UNSPECIFIED (ProtoBuf does not serialize default values, ie. 0, and so enum values are shifted by 1).

There is a unit test in SchedulerTests.cs that checks that the Bxl enums and these ProtoBuf enums match (including the shift by 1).
If you ever introduce new enum values in Bxl, that test will likely error out if you do not also add a corresponding enum value to the ProtoBuf side.