#!/bin/bash

MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)

source "$MY_DIR/env.sh"

readonly sandboxLoadScript="${MY_DIR}/sandbox-load.sh"
print_warning "This script is deprecated. Please use ${sandboxLoadScript} sandbox-load.sh' instead"
$sandboxLoadScript "$@"