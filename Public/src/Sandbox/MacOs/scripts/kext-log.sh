#!/bin/bash

log show --last 1m --predicate 'eventMessage contains "buildXL_Sandbox"' | grep "buildXL_Sandbox" | sed 's/^.*buildXL_Sandbox ]]//g' "$@"

