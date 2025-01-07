#!/bin/bash
# The arguments will be given separated by "--": the first set of arguments are AdoBuildRunner arguments
runnerArgs=()
args=()
found_separator=false

for arg in "$@"; do
  if [ "$arg" == "--" ]; then
    found_separator=true
  elif [ "$found_separator" == false ]; then
    runnerArgs+=("$arg")
  else
    args+=("$arg")
  fi
done

if [ "$found_separator" == false ]; then
    args=${runnerArgs[@]}
    runnerArgs=()
else
    for i in "${!runnerArgs[@]}"; do
        args+=("--runner-arg")
        args+=("${runnerArgs[$i]}")
    done
fi

# Call bxl.sh specifying the runner arguments explicitly
# Note this script should be called with the repo root as working directory
./bxl.sh "--use-adobuildrunner" "${args[@]}"