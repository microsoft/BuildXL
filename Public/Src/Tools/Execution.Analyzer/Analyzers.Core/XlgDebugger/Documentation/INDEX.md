# BuildXL eXecution LoG Debugger

XLG Debugger is a Visual Studio Code debugger extension for viewing/exploring/diagnosing/debugging BuildXL binary log files.

The extension is primarily focused on providing a comprehensive, post-mortem view of a build, as captured in the BuildXL execution log.  This information is presented in two forms:
1. in a tree view widget, and
1. via a domain-specific query language.

![Overview](images/overview.png)

The front end (the debugger extension) is distributed with the existing [DScript VSCode plugin](/Documentation/Wiki/Installation.md#dscript-visual-studio-code-plug-in); the back end (the component feeding the information to the visual debugger) comes with the BuildXL [Execution Analyzer](/Documentation/Wiki/Advanced-Features/Execution-Analyzer.md).

## [Installation](Installation.md)
  - [Installing DScript VSCode Extension](Installation.md#Installing-DScript-VSCode-Extension)
  - [Configuring XLG Debugger](Installation.md#Configuring-XLG-Debugger)
  - [Running](Installation.md#Running)
  - [Commands](Installation.md#Commands)

## [Object Model](ObjectModel.md)
  - [Root Object](ObjectModel.md#Root-Object)
  - [All Pips](ObjectModel.md#All-Pips)
  - [Process Pips](ObjectModel.md#Process-Pips)
  - [File Artifacts](ObjectModel.md#File-Artifacts)
  - [Directory Artifacts](ObjectModel.md#Directory-Artifacts)

## [Examples](Examples.md)
  - [Find pips with highest memory consumption](Examples.md#Find-pips-with-highest-memory-consumption)
  - [Find pips that produce shared opaque directories](Examples.md#Find-pips-that-produce-shared-opaque-directories)
  - [Find all the places the same output file is copied to](Examples.md#Find-all-the-places-the-same-output-file-is-copied-to)

## [Query Language](QueryLanguage.md)
  - [Key Concepts](QueryLanguage.md#Key-Concepts)
  - [Evaluation Environment](QueryLanguage.md#Evaluation-Environment)
  - [Literals](QueryLanguage.md#Literals)
  - [Properties](QueryLanguage.md#Property-Identifier)
  - [Variables](QueryLanguage.md#Variable-Identifier)
  - [Root and This Expressions](QueryLanguage.md#Root-and-This-Expressions)
  - [Map Expression](QueryLanguage.md#Map-Expression)
  - [Filter Expression](QueryLanguage.md#Filter-Expression)
  - [Range Expression](QueryLanguage.md#Range-Expression)
  - [Cardinality Expression](QueryLanguage.md#Cardinality-Expression)
  - [Function Application](QueryLanguage.md#Function-Application)
  - [Output Redirection](QueryLanguage.md#Output-Redirection)
  - [Let Binding](QueryLanguage.md#Let-Binding)
  - [Assign Statement](QueryLanguage.md#Assign-Expression)
  - [Match Operator](QueryLanguage.md#Match-Operator)
  - [Binary Operators](QueryLanguage.md#Binary-Operators)
  - [Operator Precedence](QueryLanguage.md#Operator-Precedence)
  - [Library Functions](QueryLanguage.md#Library-Functions)
