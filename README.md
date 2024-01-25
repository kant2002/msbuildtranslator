# MSBuild Translator

This is very primitive attempt to convert MSBuild project files to C# pseudo-code.
I hope that created file, allow for easier navigation of the inner working of .NET SDK

## How to run

```
dotnet run --project msbuildtranslator -- myproject.proj
```

For saving generated code to file pass output pass as second argument:

```
dotnet run --project msbuildtranslator -- myproject.proj Generated.cs
```

Sample of output can be found here.
https://github.com/kant2002/MSBuildScript-sample
