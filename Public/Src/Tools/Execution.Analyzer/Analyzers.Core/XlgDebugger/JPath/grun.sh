#!/bin/bash

set -e 

function cleanup {
    echo "Cleaning up..."
    rm -f *.java *.class
    echo "Done!"
}

trap cleanup SIGINT

echo "Generating..."
java \
  -cp $CLASSPATH:antlr-4.7.2-complete.jar   \
  -Xmx500M                                  \
  org.antlr.v4.Tool                         \
  JPath.g4

echo "Compiling..."
javac \
  -cp $CLASSPATH:antlr-4.7.2-complete.jar   \
  JPath*.java

while [[ true ]]; do
  echo "Running.  Enter expression then press Ctrl+D.  Press Ctrl+C to exit."
  java \
    -cp $CLASSPATH:antlr-4.7.2-complete.jar   \
    -Xmx500M                                  \
    org.antlr.v4.gui.TestRig                  \
    JPath expr -gui
done  
