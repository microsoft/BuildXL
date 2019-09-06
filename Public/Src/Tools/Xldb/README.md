# XLDB DataStore

The XLDB datastore contains the APIs that allow a user to access Xldb (the RocksDB instance that contains all of the log information).	
It is readonly (will not add anything new to the DB), and is version independent. 
It contains protobuf objects for keys and values so that these are also forward/backwards compatible!

* The Nuget package (see below) contains this public facing api that a user can access to get data from the DB instance.
	- A user can just import the nuget package to their solution and use the APIs to crawl through the database
	- Be careful of changing API behavior and/or properly deprecating old APIs once this is published so you don't cause breaking changes for users of this Nuget package

# Updating and Maintaining the Nuget Package, DLLs, and xldnanalyzer.exe

There are several DLLs that we have created and one exe:

1. The first DLL is XLdb.dll which contains the datastore accessor (Public Facing API) 
    * The dll generating code can be found in xldb.dsc under `Public/Src/Tools/Xldb`

2. The second DLL is Xldb.Proto.dll which contains the ProtoBuf files information
    * This is used in our regular analyzer dsc, the xldb.dsc, and the xldbanalyzer exe so we thought of separating it into its own DLL.
    * The dll generating code can be found in Xldb.Proto.dsc under `Public/Src/Tools/Xldb.Proto`

3. The exe is called xldbanalyzer.exe and contains several analyzers that purely rely on the Xldb.dll and Xldb.Proto.dll to get information from Xldb. 
    * The relevant files can be found under `Public/Src/Tools/Xldb.Analyzer` and contains a .cs and a .dsc file
    * The goal of this is to be the "HelloWorld" app or entry-point that a consumer can use to analyze the xldb    instance OR can use as inspiration for creating their own analyzers
    * You do NOT need to add code to this file. It is merely meant to guide you. 
    If you would like to add any code, we will look at the PR and add it, but you have now been given the freedom to do any analyzing you want on your own machines without being tied to our codebase. 
    We recommend this second path!

The Nuget package contains Xldb.dll and everything it depends on so that a user can just import this package in their console app and begin programming.
Just using Xldb.dll would not work since it would still require other dlls to be present (ie some BXL dlls, ProtoBuf dll, and more), but the nuget package is entirely independent of anything else that may be needed.
The rolling build will automatically update this package when changes are detected, but to test locally, you can run a command like `bxl /p:[buildxl.branding]SemanticVersion=0.1.1` to generate a new local package, and then import that in a standalone console app (the generated package will be located in `Out\Debug\pkgs`).
* The code for maintaining this nuget package is under NugetPackages.dsc