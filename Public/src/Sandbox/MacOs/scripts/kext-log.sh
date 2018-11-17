#!/bin/bash

log show --last 1m --predicate 'eventMessage contains "buildxl_Sandbox"' | grep "buildxl_Sandbox" | sed 's/^.*buildxl_Sandbox ]]//g' "$@"

