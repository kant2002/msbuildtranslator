using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Locator;

var projectFile = args[0];
MSBuildLocator.RegisterDefaults();
Compile(projectFile);
void Compile(string projectFile)
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
        Console.WriteLine($"bool {targetName}Run = false;");
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

    foreach (var projectProperties in tutorialProject.Properties.Where(_ => !_.IsReservedProperty && !_.IsEnvironmentProperty))
    {
        Console.WriteLine($"var {projectProperties.Name} = \"{projectProperties.EvaluatedValue}\";");
    }

    Console.WriteLine();

    foreach (var (targetName, target) in tutorialProject.Targets)
    {
        Console.WriteLine($"void {targetName}()");
        Console.WriteLine($"{{");
        if (!string.IsNullOrWhiteSpace(target.Condition))
        {
            Console.WriteLine($"\t// if ({target.Condition})");
            Console.WriteLine($"\tif ({tutorialProject.ExpandString(target.Condition)}) {{ {targetName}Run = true; return; }}");
        }
        if (!string.IsNullOrWhiteSpace(target.DependsOnTargets))
        {
            Console.WriteLine($"\t// DependsOnTargets;");
            foreach (var d in tutorialProject.ExpandString(target.DependsOnTargets).Split(';'))
            {
                Console.WriteLine($"\tif (!{d.Trim()}Run) {d.Trim()}();");
            }
        }

        if (beforeTargets.TryGetValue(targetName, out var beforeDependencies))
        {
            Console.WriteLine($"\t// BeforeTargets;");
            foreach (var d in beforeDependencies)
            {
                Console.WriteLine($"\tif (!{d}Run) {d}();");
            }
        }

        Console.WriteLine();
        foreach (var task in target.Tasks)
        {
            var originalParameters = string.Join(", ", task.Parameters.Select((pair) => $"{pair.Key}: \"{pair.Value}\""));
            var expandedParameters = string.Join(", ", task.Parameters.Select((pair) =>
            {
                try
                {
                    return $"{pair.Key}: \"{tutorialProject.ExpandString(pair.Value)}\"";
                }
                catch (InvalidProjectFileException)
                {
                    return $"{pair.Key}: \"{pair.Value}\"";
                }
            }));
            if (originalParameters != expandedParameters)
            {
                Console.WriteLine($"\t//{task.Name}({originalParameters});");
            }

            if (!string.IsNullOrWhiteSpace(task.Condition))
            {
                Console.WriteLine($"\t// if ({task.Condition})");
                string evaluatedCondition;
                try
                {
                    evaluatedCondition = tutorialProject.ExpandString(task.Condition);
                }
                catch
                {
                    evaluatedCondition = task.Condition;
                }
                Console.WriteLine($"\tif ({evaluatedCondition})");
                Console.WriteLine($"\t{{");
                Console.WriteLine($"\t\t{task.Name}({expandedParameters});");
                Console.WriteLine($"\t}}");
            }
            else
            {
                Console.WriteLine($"\t{task.Name}({expandedParameters});");
            }
        }

        if (target.Tasks.Count > 0)
        {
            Console.WriteLine();
        }

        if (afterTargets.TryGetValue(targetName, out var afterDependencies))
        {
            Console.WriteLine($"\t// AfterTargets;");
            foreach (var d in afterDependencies)
            {
                Console.WriteLine($"\tif (!{d}Run) {d}();");
            }
        }
        Console.WriteLine($"\t{targetName}Run = true;");
        Console.WriteLine($"}}");
        Console.WriteLine();
    }

    var defaultTarget = tutorialProject.Properties.Single(_ => _.Name == "MSBuildProjectDefaultTargets");
    Console.WriteLine($"{defaultTarget.EvaluatedValue}();");
}

