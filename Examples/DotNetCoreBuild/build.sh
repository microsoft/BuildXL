#!/bin/bash

readonly MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)

source "${MY_DIR}/env.sh"

/bin/bash "${MacOsScriptsDir}/bxl.sh"       \
  --config "$MY_DIR/config.dsc"             \
  --symlink-sdks-into "$MY_DIR/sdk"         \
  --buildxl-bin "$BUILDXL_BIN"              \
  /sandboxKind:macOsKext                    \
  /disableProcessRetryOnResourceExhaustion+ \
  "$@"