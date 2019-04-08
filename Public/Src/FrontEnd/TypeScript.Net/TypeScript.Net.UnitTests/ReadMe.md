All cases were migrated from TypeScript codebase.


Composition of test cases
=
Because DScript doesn't support classes, migrated checker does not fully support them.
<br/>
As a result only tests without classes in them have been copied.

The PowerShell command that was used to get all test cases that don't have classes in it:
<br/>
$noClasses = dir | where{ ! $_.PSIsContainer} | where {(($_ | get-content) -match "class").Length -eq 0}

Folder Structure
=

There are 3 folders in this directory containing test cases:
<li>Cases: Test cases that pass so far are in the Cases folder.</li>
<br/>
<li>FailingCases: Test cases that currently fail or crash are in FailingCases folder.</li>
<br/>
<li>CrashingCases: Handful of tests cause Stack Overflow Exception.</li>

Workflow
=
As TypeScript.Net is cleaned up, more of the tests should be moved from FailingCases to Cases folder.

To enforce that all passing tests are moved to Cases, there is a test that runs the FailingCases tests, and fails if any of the tests succeed.

To get new test cases from TypeScript repo, use the CopyTests.ps1 script in this folder.
<br/>
Usage: specify BuildXL repo root and TypeScript repo root, and script will find and copy all tests with no classes that haven't already been copied.