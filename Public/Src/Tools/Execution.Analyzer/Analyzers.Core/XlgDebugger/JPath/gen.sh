#!/bin/bash

java \
  -cp $CLASSPATH:antlr-4.7.2-complete.jar   \
  -Xmx500M                                  \
  org.antlr.v4.Tool                         \
  -listener                                 \
  -visitor                                  \
  -Dlanguage=CSharp                         \
  -package BuildXL.Execution.Analyzer.JPath \
  JPath.g4
