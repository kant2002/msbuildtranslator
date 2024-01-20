using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Locator;

var projectFile = args[0];
MSBuildLocator.RegisterDefaults();
using var writer = CreateWriter();
Compile(projectFile, writer);

TextWriter CreateWriter()
{
    if (args.Length >= 2)
    {
        var outputFilePath = args[1];
        return new StreamWriter(File.OpenWrite(outputFilePath));
    }

    return Console.Out;
}

void Compile(string projectFile, TextWriter textWriter)
{
    var tutorialProject = new Project(
        projectFile,
        new Dictionary<string, string>() { { "OutputFile", "test.txt" } },
        "Current");

    Dictionary<string, List<string>> beforeTargets = new();
    Dictionary<string, List<string>> afterTargets = new();
    void AddItem(Dictionary<string, List<string>> dependencyMap, string target, string dependency)
    {
        if (dependencyMap.TryGetValue(target, out var item))
        {
            item.Add(dependency);
        }
        else
        {
            dependencyMap.Add(target, new List<string>() { dependency });
        }
    }
    foreach (var (targetName, target) in tutorialProject.Targets)
    {
        textWriter.WriteLine($"bool {targetName}Run = false;");
        if (!string.IsNullOrWhiteSpace(target.BeforeTargets))
        {
            foreach (var dependency in tutorialProject.ExpandString(target.BeforeTargets).Split(';'))
            {
                AddItem(beforeTargets, dependency.Trim(), targetName);
            }
        }
        if (!string.IsNullOrWhiteSpace(target.AfterTargets))
        {
            foreach (var dependency in tutorialProject.ExpandString(target.AfterTargets).Split(';'))
            {
                AddItem(afterTargets, dependency.Trim(), targetName);
            }
        }
    }

    string GetValue(string value)
    {
        if (value.Contains("\n"))
        {
            return $"\"\"\"{value.Replace("\\", "\\\\")}\"\"\"";
        }
        else
        {
            return $"\"{value.Replace("\\", "\\\\")}\"";
        }
    }

    foreach (var projectProperties in tutorialProject.Properties.Where(_ => !_.IsReservedProperty && !_.IsEnvironmentProperty))
    {
        textWriter.WriteLine($"var {projectProperties.Name} = {GetValue(projectProperties.EvaluatedValue)};");
    }

    textWriter.WriteLine();

    foreach (var (targetName, target) in tutorialProject.Targets)
    {
        textWriter.WriteLine($"void {targetName}()");
        textWriter.WriteLine($"{{");
        if (!string.IsNullOrWhiteSpace(target.Condition))
        {
            textWriter.WriteLine($"\t// if ({target.Condition})");
            textWriter.WriteLine($"\tif ({tutorialProject.ExpandString(target.Condition)}) {{ {targetName}Run = true; return; }}");
        }
        if (!string.IsNullOrWhiteSpace(target.DependsOnTargets))
        {
            textWriter.WriteLine($"\t// DependsOnTargets;");
            foreach (var d in tutorialProject.ExpandString(target.DependsOnTargets).Split(';'))
            {
                textWriter.WriteLine($"\tif (!{d.Trim()}Run) {d.Trim()}();");
            }
        }

        if (beforeTargets.TryGetValue(targetName, out var beforeDependencies))
        {
            textWriter.WriteLine($"\t// BeforeTargets;");
            foreach (var d in beforeDependencies)
            {
                textWriter.WriteLine($"\tif (!{d}Run) {d}();");
            }
        }

        textWriter.WriteLine();
        foreach (var task in target.Tasks)
        {
            var originalParameters = string.Join(", ", task.Parameters.Select((pair) => $"{pair.Key}: {GetValue(pair.Value)}"));
            var expandedParameters = string.Join(", ", task.Parameters.Select((pair) =>
            {
                try
                {
                    return $"{pair.Key}: {GetValue(tutorialProject.ExpandString(pair.Value))}";
                }
                catch (InvalidProjectFileException)
                {
                    return $"{pair.Key}: {GetValue(pair.Value)}";
                }
            }));
            if (originalParameters != expandedParameters)
            {
                textWriter.WriteLine($"\t/*{task.Name}({originalParameters});*/");
            }

            if (!string.IsNullOrWhiteSpace(task.Condition))
            {
                textWriter.WriteLine($"\t/* if ({task.Condition})*/");
                string evaluatedCondition;
                try
                {
                    evaluatedCondition = tutorialProject.ExpandString(task.Condition);
                }
                catch
                {
                    evaluatedCondition = task.Condition;
                }
                textWriter.WriteLine($"\tif ({evaluatedCondition})");
                textWriter.WriteLine($"\t{{");
                textWriter.WriteLine($"\t\t{task.Name}({expandedParameters});");
                textWriter.WriteLine($"\t}}");
            }
            else
            {
                textWriter.WriteLine($"\t{task.Name}({expandedParameters});");
            }
        }

        if (target.Tasks.Count > 0)
        {
            textWriter.WriteLine();
        }

        if (afterTargets.TryGetValue(targetName, out var afterDependencies))
        {
            textWriter.WriteLine($"\t// AfterTargets;");
            foreach (var d in afterDependencies)
            {
                textWriter.WriteLine($"\tif (!{d}Run) {d}();");
            }
        }
        textWriter.WriteLine($"\t{targetName}Run = true;");
        textWriter.WriteLine($"}}");
        textWriter.WriteLine();
    }

    var defaultTarget = tutorialProject.Properties.Single(_ => _.Name == "MSBuildProjectDefaultTargets");
    textWriter.WriteLine($"{defaultTarget.EvaluatedValue}();");
}
