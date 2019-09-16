# XLDB DataStore

The XLDB datastore contains the APIs that allow a user to access Xldb (the RocksDB instance that contains all of the log information).	
It is readonly (will not add anything new to the DB).
It will happily read in any Xldb version that is equal to or less than the current version. 
It contains protobuf objects for keys and values so that these are also forward/backwards compatible.

> The Xldb version is updated upon a breaking change in the underlying datastore or indexing (i.e. switching out of RocksDB or changing keys for a value).
 
* A Nuget package (see below) contains this public facing api that a user can access to get data from the DB instance.
	- A user can just import the Nuget package to their solution and use the APIs to crawl through the database
	- Be careful of changing API behavior and/or properly deprecating old APIs once this is published so you don't cause breaking changes for users of this Nuget package

All public facing API endpoints can be found under `IXldbDataStore.cs` with the neccessary comments and documentation.

The datastore can be consumed as a Nuget package which contains all the neccesary dlls. Alternatively, you can build Bxl and then copy over the neccesary dlls yourself into your code.

<br>

# Adding Other Bindings

Currently we only have C# bindings for the datastore and do not have any plans on adding other bindings in the near future. 
**However** if you would like to add a binding, say in Python or some other language that supports RocksDB and ProtoBuf, please feel free to make a PR and add it to our codebase. 
Others may find these bindings useful as well.

For help and assistance in creating these bindings, reach out to a team member, and look through `XldbDataStore.cs` to see how we handle things like PrefixSearching keys and how we use column families to partition the data into more logical columns (think of them as tables from a SQL standpoint). 