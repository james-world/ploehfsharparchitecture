#An FSharp Functional Architecture

My run through of Mark Seeman's FSharp Architecture Pluralsight Course that presents a Web API implemented in FSharp in a functional style using pipes and filters.

The original code used a bit of undisclosed "foo" to make the web project work. I converted the code to use the fsharp project OWIN+Katana template and brought it up to date with current nuget packages.

There are some tags you might find useful:

- EndOfModule3 - where Mark branches to do file based and azure based persistence.
- FilePersistence - with file based persistence
- AzurePersistence - with azure persistence - this will work against the Azure Storage Emulator and you can also publish the project to Azure as well.
