# BuildXL Extension for Visual Studio Code
For BuildXL version: 0.0.0

Welcome to the Microsoft Build Accelerator DScript extension for Visual Studio Code - the productivity tool for anyone working with DScript!

In addition to standard features expected from a modern code editor like Syntax Highlighting and Automatic Statement Completion, this release adds the following features for DScript:

- Go to Definition (F12)
- Peek Definition (Alt+F12)
- Find All References (Shift+F12)
- Rename Symbol (F2)
- Change All Occurrences (Ctrl+F2)
- Format Document (Alt+Shift+F)

We would like to extend this plugin with support for target langauges like C++, C# and others. 
We have a prototype that uses vscode-cpptools extension for C++ But Omnisharp does not yet have a bridge back to vscode. We'll likely hold off until [Build Server Protocol](https://github.com/scalacenter/bsp) is a bit more established and hook into that.

## Found a bug?
Please file any issues at https://github.com/Microsoft/BuildXL/issues
