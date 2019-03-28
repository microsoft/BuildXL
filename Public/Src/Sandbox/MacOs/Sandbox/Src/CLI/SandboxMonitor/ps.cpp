// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "ps.hpp"
#include <array>
#include <sstream>
#include <regex>

set<string> ps_keywords =
{
    "%cpu", "%mem", "acflag", "args", "comm", "command", "cpu", "etime", "flags", "gid", "inblk", "jobc", "ktrace",
    "ktracep", "lim", "logname", "lstart", "majflt", "minflt", "msgrcv", "msgsnd", "nice", "nivcsw", "nsigs", "nswap",
    "nvcsw", "nwchan", "oublk", "p_ru",  "paddr", "pagein", "pgid", "pid", "ppid", "pri", "re", "rgid", "rss", "ruid",
    "ruser", "sess", "sig", "sigmask", "sl", "start", "state", "svgid", "svuid", "tdev", "time", "tpgid", "tsess",
    "tsiz", "tt", "tty", "ucomm", "uid", "upr", "user", "utime", "vsz", "wchan", "wq", "wqb", "wqr", "wql", "xstat"
};

string exec(const char *cmd)
{
    array<char, 128> buffer;
    string result;
    shared_ptr<FILE> pipe(popen(cmd, "r"), pclose);
    if (!pipe) return "";
    while (!feof(pipe.get()))
    {
        if (fgets(buffer.data(), 128, pipe.get()) != nullptr)
            result += buffer.data();
    }
    return result;
}

string ps(pid_t pid, const string &cols)
{
    stringstream str;
    str << "ps -p " << pid << " -o " << cols;
    string stdout = exec(str.str().c_str());
    return regex_replace(stdout, regex("\\s*\n$"), "");
}
