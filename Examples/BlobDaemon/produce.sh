#!/bin/bash
# Writes a small file to the path given as the first argument ($1).
# The path is provided by BuildXL via Artifact.output, so this script never hard-codes or derives paths.
echo "BlobDaemon-validation-OK" > "$1"
