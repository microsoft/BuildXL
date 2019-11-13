bxlAnalayzer.exe is a tool that lives next to bxl.exe. It can provide analysis of the execution of a build and its graph. It operates on execution log files (.xlg), which live in BuildXL's log directory.

The analyzer works on both on Windows and macOS (unless otherwise specified). For macOS, remove the ".exe" extension from the examples and correct the paths.

## Specifying inputs
Most analysis modes require a pointer to an execution log from a prior BuildXL build session. This can either be specified on the command line with `/xl:[PathTo.xlg file]` or the analyzer will find the log of the last BuildXL sesison run if left blank. See the analyzer help text for more details

# Analysis modes
The analyzer application has many different modes added by the BuildXL team as part of the core product as well as other consumers of BuildXL. See the help text for a full listing of the various analyzer modes: `bxlanalyzer.exe /help`. Specify the mode using the `/m:` option.

## Critical Path Analysis
This analysis shows you the top 20 critical paths in the invoked build. It only considers the time something actually took to execute. So if some pips were from cache, their full non-cached execution time won't be reflected in the critical path. 

`bxlAnalyzer.exe /m:criticalpath /xl:F:\src\buildxl\Out\Logs\20170227-121907\BuildXL.xlg /outputfile:criticalpath.txt`

## Dump Pip Analysis
This analyzer dumps details of a specific pip. It is helpful for getting the command line, environment variables, which files/directories were untracked, etc.

`bxlAnalyzer.exe /m:DumpPip /pip:Pip6E0AB6802406618E /xl:/Users/user01/build/Logs/devmain/BuildXL/BuildXL.xlg /outputFile:out.html`

## Cache Miss Analysis
[Cache Miss Analysis](./Cache-Miss-Analysis.md)
