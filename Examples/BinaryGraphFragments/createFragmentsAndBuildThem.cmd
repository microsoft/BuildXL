@echo off

rem Set the common output dir (used by individual fragments in the first build and later by second build).
set OUTPUT_DIR=%cd%\ComposedGraph\Out\Bin

rem Execute the first build. Generated fragments will be located under 'out' (.\GraphFragments\out\bin) mount that is defined in config.dsc.
%BUILDXL_BIN%\bxl /c:GraphFragments\config.dsc

rem Store the fragment location into an env var, so the second build can locate them.
set GRAPH_FRAGMENTS_LOCATION=%cd%\GraphFragments\Out\Bin

rem Execute the second build (it will load the graph fragments and execute pips defined in them).
rem The outputs will be located under %OUTPUT_DIR%.
%BUILDXL_BIN%\bxl /c:ComposedGraph\config.dsc