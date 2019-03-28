// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "arg_parse.hpp"

// Define config (name, type, default-value) tuples
#define ALL_ARGS(m)                            \
  m(help,        bool,   false)                \
  m(delay,       int,    1)                    \
  m(col_sep,     string, ", ")                 \
  m(stacked,     bool,   false)                \
  m(no_header,   bool,   false)                \
  m(interactive, bool,   false)                \
  m(ps_fmt,      string, "%cpu,%mem,ucomm")

GEN_CONFIG_DECL(ALL_ARGS)

void ConfigureArgs() {
    Config::argMeta(kArg_help)
        ->LongName("help")
        ->ShortName("h")
        ->Description("Print help and exit.");
    
    Config::argMeta(kArg_delay)
        ->LongName("delay")
        ->ShortName("s")
        ->Description("Delay between updates in seconds.");
    
    Config::argMeta(kArg_col_sep)
        ->LongName("column-separator")
        ->ShortName("sep")
        ->Description("Column separator.");
    
    Config::argMeta(kArg_stacked)
        ->LongName("stacked")
        ->ShortName("t")
        ->Description("Group printed processes by ClientId and PipId.");

    Config::argMeta(kArg_no_header)
        ->LongName("no-header")
        ->ShortName("nh")
        ->Description("Don't print header line.");
    
    Config::argMeta(kArg_interactive)
        ->LongName("interactive")
        ->ShortName("i")
        ->Description("Runs the monitor continuously until interrupted.");
    
    Config::argMeta(kArg_ps_fmt)
        ->LongName("ps-fmt")
        ->ShortName("f")
        ->Description("Process info to display. The format is the same as for the '-o' option of the 'ps' program.");
}


