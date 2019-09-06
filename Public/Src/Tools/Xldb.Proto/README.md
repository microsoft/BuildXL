# Generic ProtoBuf details and Implementation

This directory contains all of the proto files that represent the ojects stored in XLDB.
For some guidelines and information regarding ProtoBuf, check out these two links:

[ProtoBuf 3 Guide](https://developers.google.com/protocol-buffers/docs/proto3)

[ProtoBuf Style Guide](https://developers.google.com/protocol-buffers/docs/style)

They are split as follows:

We store serialized ProtoBuf objects as the keys and values in RocksDB, and due to our use of PrefixSearch to query some data stored, there are some restrictions on what keys can contain (values can contain anything they want, including nested messages). 

### Restrictions:
1. The values in the Key must be in order from most general to most specific. This way the prefix search can grab the general information first and then you can scope in as needed with more specific values.

2. There cannot be nested messages within the Key (primitives only). The reason is ProtoBuf will _prepend_ bytes with information related to nested message lengths when serializing, and if these lengths differ between different keys, the prefix search will not work at all.

A key thing to keep in mind when changing any proto file is that the order of messages matters heavily to keep things forward and backwards compatible. Be careful when adding new fields and deprecating old ones! Furthermore default values (int = 0, bool = false, string = "", message=null, etc) are not serialized on the wire to save space, and the string representation of these Protobuf objects will not deserialize the default values either (though the instantiated class WILL have the values). 

* Do not be alarmed if the value is missing, it means its either unassigned or it is the default value.
* To force these values to be printed out in C#, you can `JToken.Parse(JsonConvert.SerializeObject(proto_obj, Formatting.Indented))`

<br>
<br>

# ProtoBuf Files:

## Events.Proto

The Events.proto file contains all of the protobuf schemas for the events (ie. Things parsed from the .xlg file).

The key is defined by the message `EventKey` and the values are the different event messages.

## StaticGraph.proto

The StaticGraph.proto file contains metadata about the static graph as well as the keys for pips. 
We have a special indexing style for pips in that we can search by SemistableHash or by PipId. 
To do this, we associate a SemistableHashKey -> PipIdValue. 
We extract the pipId from there, and create a PipIdKey that points to the actual Pip payload value.

To summarize, we can either do SemistableHash -> PipId -> Pip, *OR* we can do PipId -> Pip. 
Both ways are exposed to the user in the API.

This file also contains messages related to Producer/Consumer indexing that we created where we can quickly find all producers or consumers for a given File or Directory path.

## HelperStructs.proto

The HelperStructs.proto file contains a bunch of BXL structs -> ProtoBuf messages that are used by the Events.Proto and StaticGraph.proto files.
These messages include things like AbsolutePath, FileArtifact, DirectoryArtifact, and more.

## Enums/*.proto

This directory contains all of the enums have been ported over from the BXL codebase.
Each enum is approximately shifted by 1 (either + 1 or << 1) relative the BXL enums since ProtoBuf guidelines require the 0 value for each enum to be UNSPECIFIED (ProtoBuf does not serialize default values, ie. 0, and so enum values are shifted by 1).

There is a unit test in SchedulerTests.cs that checks that the BXL enums and these ProtoBuf enums match (including the shift by 1).
If you ever introduce new enum values in BXL, that test will likely error out if you do not also add a corresponding enum value to the ProtoBuf side.