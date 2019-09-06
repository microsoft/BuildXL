# XLDB DataStore

The XLDB datastore contains the APIs that allow a user to access Xldb (the RocksDB instance that contains all of the log information).	

# XLG to DB Analyzer

The XLG to DB Analyzer (XLGToDBAnalyzer.cs) crawls the XLG file and using the three proto files it properly populates a RocksDB database with the event data.
	- For reference on the key/value pairs, check the proto files
	- The aim of this analyzer is to allow for fast traversals for relevant data after it has been dumped into the DB
	- Furthermore, the data in the DB no longer relies on a particular version of BuildXL to be opened, so we no longer have this constraint either

This analyzer also dumps all pipgraph and piptable information into the database (with the paths and strings expanded, so there is no reason to store PathTable or StringTable separately).

There are also some auxiliary dictionaries that are serialized to the database for fast "indexing", one of them being getting all the producers and consumers of a particular path (file or directory).

#XldbDataStore

This is the public facing api for all of the data stored in the database. It is readonly (will not add anything new to the DB), and is version independent. It contains protobuf objects for keys and values so that these are also forward/backwards compatible!
	- The Nuget package (see below) contains this public facing api that a user can access to get data from the DB instance.
	- A user can just import the nuget package to their solution and use the APIs to crawl through the database
	- Be wary of changing API behavior or properly deprecating old APIs once this is published so you don't cause breaking changes for users of this Nuget package


# RocksDB Indexing "Hack"

RocksDB is a key-value store, which means it does not support indexing. We can do a prefix search for a key which is fine, but can be expensive. In addition to this, we have an idea where we can create a nested index so that based on what parameters we are interested in, we can create and index key to a different index value which serves as a key to the final value.
	- This can have several layers of indirection, and the design is still being worked out over time.
	- One such index is for pips where we have semistablehash -> pipid -> pip, but one can also just do pipid -> pip

# Updating and Maintaining the Nuget Package, DLLs, and xldnanalyzer.exe

There are several DLLs that we have created and one exe. 
	1. The first DLL is XLdb.dll which contains the datastore accessor. 
		a. This can be found in xldb.dsc under Public/Src/Tools/Xldb
	2. The second DLL is Xldb.Proto.dll which contains the protobuf files information
		a. This is used in our regular analyzer dsc, the xldb.dsc, and the xldbanalyzer exe so we thought of separating it into its own DLL.
		b. This can be found in Xldb.Proto.dsc under Public/Src/Tools/Xldb.Proto
	3. The exe is called xldbanalyzer.exe and contains several analyzers that purely rely on the Xldb.dll and Xldb.Proto.dll to get information. 
		a. The relevant files can be found under Public/Src/Tools/Xldb.Analyzer and contains a .cs and a .dsc file
		b. The goal of this is to be the "HelloWorld" app or entry-point that a consumer can use to analyze the xldb instance OR can use as inspiration for creating their own analyzers
		c. You do NOT need to add code to this file. It is merely meant to guide you. If you would like to add any code, we will look at the PR and add it, but you have now been given the freedom to do any analyzing you want on your own machines without being tied to our codebase. We recommend this second path!

Now the nuget package contains Xldb.dll and everything it depends on so that a user can just import this package in their console app and begin programming. Just using Xldb.dll would not work since it would still require other dlls to be present (ie some bxl dlls, protobuf dll, and more), but the nuget package is entire independent of anything else that may be needed. The rolling build will automatically update this package when changes are detected, but to test locally, you can run a command like `bxl /p:[buildxl.branding]SemanticVersion=0.1.1` to generate a new local package, and then import that in a standalone console app (the generated package will be located in Out\Debug\pkgs).
	- The code for maintaining this nuget package is under NugetPackages.dsc
## Adding a new classical analyzer

For now, the classical analyzers will continue to be supported. However we do not necessarily urge devs to add to these, and instead urge them to use Xldb if possible. If you would like to make a classical analyzer, follow these instructions:

To add a new Analyzer, one can take a look at the example analyzers under `Public/Src/Tools/Execution.Analyzer/Analyzers.Core`
	- Use one of the examples to create your own analyzer file that has the internal partial class Args and the internal sealed class for your Analyzer that extends the base Analyzer class
	- Go to AnalysisMode.cs and add your analyzer to the enum. This is what will be used in the "mode" flag. 
	- Go to Args.cs (also in Execution.Analyzer directory) and add the few lines of code that call your analyzer's WriteHelp function and also initialize your Analyzer
	- Modify the rest of your Analyzer as desired by overriding methods like Analyze, Prepare, etc.

