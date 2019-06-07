#!/bin/bash

## ====================================================================
## NOTE: This file must have UNIX line endings (LF instead of CRLF)
## ====================================================================

function MyDir {
    cd `dirname ${BASH_SOURCE[0]}` && pwd
}

# File 'env.sh' is copied from 'ExampleBuild' by the deployment script (CoreDotNetTests.dsc)
source "$(MyDir)/env.sh"

# Extract an attribute value from an XML element definition
#
# @param attrName
#   - XML attribute name.  Must not contain spaces or any other wacky characters.
#
# @param line
#   - A single line of text representing an XML element header.
#     If "-" is passed, lines are read from stdin.
#
# @return
#   - Prints out the value of the given attribute in the given XML element.  It does so
#     by grepping for ' <attrName>="<anything-without-quotes>"' and then extracting the quoted value.
function extract_attr_value { # (attrName, line)
    attrName=$1
    line="$2"

    function _grep_line { 
        echo $line | grep -o " ${attrName}=\"[^\"]*\"" | sed -e 's/^[^"]*"//g' -e 's/"$//g'
    }

    if [[ "$line" == "-" ]]; then
        while read line; do _grep_line; done
    else
        _grep_line
    fi
}

# Takes an XML file and returns the line containing the header of the '$elementName' element.
#
# @param elementName
#   - XML element name
#
# @param xmlFile
#   - XML file.
#
# @return
#   - Prints out the line containing the header of the '$elementName' element.
function extract_xml_element { # (elementName, xmlFile)
    elementName="$1"
    xmlFile="$2"
    grep -o "<$elementName [^>]*>" $xmlFile
}

# Extracts some statistics from an XUnit XML output file
#
# @param xunitXmlFile
#   - Name of the XUnit output XML file (specified to XUnit via the -xml option)
#
# @return
#   - Extracts statistics from the given file and prints them out in the following format:
#     "Time: %7ss | Passed: %4s | Failed: %3s | Skipped: %3s | Errors: %3s"
#   - Returns 0 if both "Failed" and "Errors" numbers are "0"; otherwise returns 1.
function extract_xunit_stats { # (xunitXmlFile)
    xunitXmlFile=$1

    if [[ ! -f $xunitXmlFile ]]; then
        printf "XUnit XML results file not found: '%s'" "$xunitXmlFile"
        return
    fi

    statsLine=$(extract_xml_element "assembly" "$xunitXmlFile")
    asmName=$(extract_attr_value    "name"    "$statsLine")
    numPassed=$(extract_attr_value  "passed"  "$statsLine")
    numFailed=$(extract_attr_value  "failed"  "$statsLine")
    numSkipped=$(extract_attr_value "skipped" "$statsLine")
    numErrors=$(extract_attr_value  "errors"  "$statsLine")
    exeTime=$(extract_attr_value    "time"    "$statsLine")

    traits=$(extract_xml_element "trait" "$xunitXmlFile" | sort | uniq | extract_attr_value "value" - | paste -sd, -)
    if [[ -z $traits ]]; then
        traitsStr=""
    else
        traitsStr="[$traits]"
    fi

    printf "Time: %7ss | Passed: %4s | Failed: %3s | Skipped: %3s | Errors: %3s | %s %s" $exeTime $numPassed $numFailed $numSkipped $numErrors $asmName "$traitsStr"
    test $numFailed -eq 0 -a $numErrors -eq 0
}

# Runs an XUnit test
#
# @parame folderName
#   - Deployment folder of the test name.
#
# @param dllName
#   - Name of the test DLL.
#
# @param ...extraXunitArgs
#   - Optional additional args to pass to XUnit
#
# @return
#   - Prints out progress while executing tests
#   - Returns the exit code of the XUnit invocation
function run_xunit { #(folderName, dllName, ...extraXunitArgs)
    folderName=$1
    dllName=$2
    shift
    shift

    extraXunitArgs=()
    while [[ $# -gt 0 ]]; do 
        extraXunitArgs+=("$1")
        shift
    done

    if [[ ! -d "$folderName" ]]; then
        print_error "'$folderName' is not a folder"
        return -1
    fi

    xunitStdoutFname="${dllName}.xunit.stdout"
    xunitStderrFname="${dllName}.xunit.stderr"
    xunitResultFname="${dllName}.result.xml"

    pushd $(pwd) > /dev/null
    cd "$(MyDir)/$folderName"

    # delete any previously left xunit result file because XUnit appends to it
    rm -f ${xunitResultFname}

    # run XUnit
    echo "${tputBold}[Running]${tputReset} $dllName ..."
    dotnet xunit.console.dll $dllName -nocolor -parallel none -xml $xunitResultFname -noappdomain -noTrait "Category=WindowsOSOnly" -noTrait "Category=WindowsOSSkip" -noTrait "Category=Performance" -noTrait "Category=QTestSkip" -noTrait "Category=DominoTestSkip" -noTrait "Category=SkipDotNetCore" ${extraXunitArgs[@]} >$xunitStdoutFname 2>$xunitStderrFname
    exitCode=$?

    # extract statistics from XUnit's XML result file
    stats=$(extract_xunit_stats $xunitResultFname)

    # add a note to rendered dll name if we are running select test classes only
    if [[ -z ${extraXunitArgs[@]} ]]; then
        statsToRender=$stats
    else
        statsToRender="$stats (*** select classes only ***)"
    fi

    if [[ "$exitCode" -eq "0" ]]; then
        echo "${tputLineUp}${tputGreen}(passed)${tputReset} ${statsToRender}${tputClearLine}"
    else
        echo "${tputLineUp}${tputRed}(failed)${tputReset} ${statsToRender}${tputClearLine}"
        cat $xunitStderrFname
        echo "  XUnit log:"
        echo "    $(pwd)/${xunitResultFname}"
        echo ""
        cat $xunitResultFname
        echo ""
    fi

    popd > /dev/null

    return $exitCode
}
