if (!exists("_col"))   _col="Cpu Percent"
if (!exists("_files")) _files=""
if (!exists("_term"))  _term="svg"

set term _term

set key autotitle columnhead
set datafile separator comma

getTimeFormat(timestr) = strlen(timestr) <= 7 ? "[%M:%S]" : "[%H:%M:%S]"

buildXLTimeCol(columnIndex) = timecolumn(columnIndex, getTimeFormat(stringcolumn(columnIndex)))

set xdata time
set grid

plot for [file in _files] file using (buildXLTimeCol(1)):_col with lines 


