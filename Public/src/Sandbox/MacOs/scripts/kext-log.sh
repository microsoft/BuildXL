#!/bin/bash

log show --last 1m --predicate 'eventMessage contains "domino_Sandbox"' | grep "domino_Sandbox" | sed 's/^.*domino_Sandbox ]]//g' "$@"

