
# XLG to DB Analyzer

The XLG to DB Analyzer (`XLGToDBAnalyzer.cs`) crawls the XLG file and other Graph MetaData files, and using the proto files it populates a RocksDB database with converted data.
* For reference on the key/value pairs, check the proto files or the README.md under `../Xldb.Proto`
* The aim of this analyzer is to allow for fast traversals for relevant data after it has been dumped into the DB
* Furthermore, the data in the DB no longer relies on a particular version of BuildXL to be opened, so we no longer have this constraint either

This analyzer also dumps all pipgraph and piptable information into the database (with the paths and strings expanded, so there is no reason to store PathTable or StringTable separately).

There are also some auxiliary dictionaries that are serialized to the database for fast "indexing", one of them being getting all the producers and consumers of a particular path (file or directory).

The file `XldbProtobufExtensions.cs` contains helper methods that can convert Bxl classes, structs, and enums into the ProtoBuf equivalent messages.

<br>

# Adding a new classical analyzer

For now, the classical analyzers will continue to be supported. However we do not necessarily urge devs to add to these, and instead urge them to use Xldb if possible. If you would like to make a classical analyzer, follow these instructions:

To add a new Analyzer, one can take a look at the example analyzers under `Public/Src/Tools/Execution.Analyzer/Analyzers.Core`
* Use one of the examples to create your own analyzer file that has the internal partial class Args and the internal sealed class for your Analyzer that extends the base Analyzer class
* Go to AnalysisMode.cs and add your analyzer to the enum. This is what will be used in the "mode" flag. 
* Go to Args.cs (also in Execution.Analyzer directory) and add the few lines of code that call your analyzer's WriteHelp function and also initialize your Analyzer
* Modify the rest of your Analyzer as desired by overriding methods like Analyze, Prepare, etc.

