# Overview

BuildXL builds can be filtered by requesting a subset of the files produced in the build. Based on the files requested, BuildXL will run the minimal set of pips to produce those files.

## Filter Tuples 
Filters are defined as a tuple of filter type and filter argument. For example: <code>output='myApp.exe'</code>. The filter tuple is evaluated against all Pips in the build graph to produce a set of files. In this example, the filter type is `output` and the argument for that filter is <code>myApp.exe</code>. The filter argument must always be contained within single quotes.

## Combining tuples
Filters can have many tuples to build a more complicated expression. Each tuple evaluates to a set of files. Those sets are then combined with other sets using <code>and</code> (set intersection) and <code>or</code> (set union).

Expressions are evaluated left to right. You can include parenthesis to control the evaluation order.

## Selecting dependencies
After the entire filter operation has been evaluated to a set of files, the corresponding producing pips are found. All consumed dependencies of those pips are implicitly scheduled as well, since it isn't possible to run a pip without having its dependencies' outputs. However, those files are not explicitly requested. That means they may not be updated on disk, as a performance optimization. This depends on the <code>/enableLazyOutputs</code> option. By default, the option is enabled.

##Negation
Negation works by taking all files in the graph that are not matched by the negated filter. So for example, if a graph has: [Pip1, Pip2, Pip3] and a filter expression matches [Pip2], that negated expression will match [Pip1, Pip3].

The result of a subset of a filter expression can be negated by prepending it with: <code>~</code>. To avoid ambiguity, the expression set must be surrounded by parenthesis in order to use negation. For example, this will include all pips that do not have the test tag: <code>~(tag='test')</code>. It is important to note though that if there is some pipA without the test tag that depends on pipB with the test tag, both pipA & pipB will end up being run, even though pipB has the test tag. That is because pipB must be run in order to satisfy the dependencies of pipA.

## Filter functions
Filter functions can be used to have more granular control over what gets included. They operate outside of a filter tuple or set of filter tuples. Negation, described above, is essentially a function. Additionally these functions exist:

1. dpt - Include outputs of transitive closure of inner filter dependents in the filter (e.g. `dpt(tag='test')` to include all dependents of pips tagged with the test tag). This is useful to address the caveat of the negation example above. `~(dpt(tag='test'))` will find all pips tagged with test, find their dependents, and negate that set to run all other pips. This effectively prevents any pip tagged as test from being run. It can however exclude pips that are not tagged with test.
1. dpc - Include outputs of transitive closure of inner filter dependencies in the filter (e.g. `dpc(tag='check')` to include all dependencies of pips tagged with the check tag)
1. copydpt - Include outputs of filter along with all downstream copies of the outputs via copy pips.
1. requiredfor - Include the immediate inputs (outputs of direct dependencies) for the pips specified filter.

## Filter types
1. id - Finds a pip based on its stable ID and matches all produced files.
1. input - Path filter. Finds all pips consuming an input file and then matches all output files produced by those pips.
1. output - Path filter. This matches one or more output files.
1. spec - Path Filter. This matches pips based on the specification file the pip is defined in. Once a matching pip is found, all produced output files are matched.
1. tag - Finds pips annotated with the tag and matches all produced files. Tags are case sensitive
1. value - Finds the pips with a corresponding output value name and matches all produced files. This filter does not traverse though value to value edges. So for example: if it matched a value that was a list of other values, the values in that list would not also be matched.
1. valueTransitive - The same as the value filter, but it will traverse through value to value dependencies. This is sometimes desired, like if the value requested isn't an output value but is a list of other value references. But generally using this filter to request an output is an over specification.
1. specref - Similar to the basic spec filter, but transitively includes specs of pips referenced by pips in target spec(s) of the filter. For example, if SpecB refers to a specific output in SpecA, performing a specref filter for SpecB will cause all pips in SpecA to be included, not the minimal set of pips needed to fulfill the dependencies of SpecB.

## Path based filters
Path filters by either be absolute or relative to the current directory of the bxl.exe process. The two filters below are equivalent, assuming bxl.exe is being run from d:\src\buildxl
 output='d:\src\buildxl\out\tools\myTool\tool.exe'
 output='out\tools\myTool\tool.exe'

A filter may use `.` to match all files directly within a directory:
`output='d:\out\bin\tools\myTool\.'`

A filter may use `*` to match all files in a directory and all subdirectories:
`output='d:\out\bin\tools\*'`

### Filtering opaque directories
The case of filtering opaque directories has a caveat: the content produced in an opaque directory is unknown before running a build, and therefore the behavior of a filter based on the content of an opaque directory cannot be applied reliably. BuildXL makes an assumption in this case: a filter explicitly going underneath the root of an opaque directory will match any pip contributing to that directory, independently of the actual files the pip produces under the opaque directory. 

For example, let's consider this filter:
`output='d:\out\bin\tools\outputDirectory\myFile.txt'`

If a pip has an output directory `d:\out]bin\tools\outputDirectory` and when running produces `d:\out\bin\tools\outputDirectory\myFile.txt`, then it will be included in the filter. This is kind of natural. However, if the pip does not produce `myFile.txt` but instead `myOtherFile.txt`, it will be included anyway, since at filtering time BuildXL only knows that the pip is going to produce *some* file in this output directory, but it doesn't know which ones.

As long as the filter is used in a context with no negations, this assumption means BuildXL will over-approximate and may include more pips than the ones strictly specified. But beware that filtering on an output directory in a context with negations (e.g. `~(output='d:\out\bin\tools\outputDirectory\myFile.txt')`), some pips may be excluded unexpectedly.

##Implicit filters
It is convenient to use a shorthand to create a filter based on a path or a part of the path. Any bxl.exe argument that doesn't have a leading - or / will be interpreted as an implicit filter. These filters match any output or spec file. Once translated, they follow all of the same rules as the filter they are translated into. For example:
 bxl.exe myproj.exe
is equivalent to:
 bxl.exe "/f:output=*'\myproj.exe' or spec='\*myproj.exe'"

## Examples

Consider 3 pips:
```
PipA:
  id=AAAAAAAAAAAAAAAA
  tag={test, vstest.console.exe}
  spec=c:\src\buildxl\mytest.ds
  output=c:\out\test\testresult.txt
PipB:
  id=BBBBBBBBBBBBBBBB
  tag={product, csc.exe}
  spec=c:\src\buildxl\product.ds
  output=c:\out\bin\product.dll
PipC:
  id=CCCCCCCCCCCCCCCC
  tag={csc.exe, legacy}
  spec=c:\src\buildxl\originalProduct.ds
  output=c:\out\bin\originalProduct.dll
```

Matches PipB, PipC and dependencies:
`/filter:output='c:\out\bin\*'`


Matches PipB and dependencies:
` /filter:output='*\product.dll'`


Matches PipB and dependencies:
` /filter:id='BBBBBBBBBBBBBBBB'`


Matches PipA and dependencies:
` /filter:tag='test'`


Matches None. Note: tag filter is case sensitive:
` /filter:tag='TEST'`


Matches PipB, PipC and dependencies:
` /filter:~(tag='test')`


Matches PipB, PipC and dependencies:
` /filter:id='AAAAAAAAAAAAAAAA ' or id='BBBBBBBBBBBBBBBB'`


Matches PipB, PipC and dependencies:
` /filter:((id='AAAAAAAAAAAAAAAA ' or id='BBBBBBBBBBBBBBBB') or id='CCCCCCCCCCCCCCCC')`


Matches PipB and dependents:
` /filter:dpt(id='BBBBBBBBBBBBBBBB')`

## EBNF
```
(*BuildXL PipFilter expressions*)
pipFilter = [dependencySelection](*When unspecified, all dependencies will be run*), filter ;
dependencySelection =  "+" | (*Specifies dependents should be run*)
                       "-" ; (*Specifies dirty dependencies. This means dependencies are presumed up to date*)
filter = filterType, "=", filterArgument |
         [filterFunction], "(" filter, binaryOperator, filter, ")" ;
binaryOperator = "and" | "or" ;
filterFunction = "~"   | (*negation*)
                 "dpt" | (*all transitive dependents*)
                 "dpc" ; (*all transitive dependencies*)
filterType = "id"  |               (*Unique identifier given to a pip*)
             "input" |             (*Any input file of the pip*)
             "output" |            (*Any outputut file of the pip*)
             "spec" |              (*The spec file producing the pip*)
             "specref" |             (*The spec file producing the pip*)
             "spec_valueTransitive" | (*The spec file producing the pip*)
             "tag" |               (*Descriptive tag given to pips*)
             "value" |             (*Value name*)
             "valueTransitive" ;   (*Value name*)
filterArgument = "'", {alphaNumericCharacters} | pathArgument, "'" ;
pathArgument = "mount[", { alphaNumericCharacters } + "]" |
               { pathCharacters } |        (*Full or relative path to a file*)
               { pathCharacters }, "\*" |  (*Full or relative path to a directory. Recursively matches file in the directory*)
               { pathCharacters }, "\." |  (*Full or relative path to directory. Matches files immediately in the directory*)
               "*\", { pathCharacters } ;  (*Matches a filename regardless of where the file occurs*)
alphaNumericCharacters = "A" | "B" | "C" | "0" | "1" | "2" (*Truncated for brevity*)
pathCharacters = alphaNumericCharacters | "\" | "!" | "@" | "#" | "$" | "%" | "(" | ")" | "+" | "-" | "_" | "{" | "[" | "}" | "]" | ";" | "," | "." ;
```
