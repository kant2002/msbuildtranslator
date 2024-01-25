using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using Microsoft.Build.Locator;
using Microsoft.Win32;
using System.CodeDom.Compiler;
using System.IO;
using System.Threading.Tasks;

var projectFile = args[0];
MSBuildLocator.RegisterDefaults();
using var writer = new IndentedTextWriter(CreateWriter());
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

void Compile(string projectFile, IndentedTextWriter textWriter)
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

    foreach (var projectItemDefinition in tutorialProject.ItemDefinitions.Values)
    {
        textWriter.WriteLine($"class {projectItemDefinition.ItemType}");
        textWriter.WriteLine($"{{");
        using (var _ = new IndentationScope(textWriter))
        {
            foreach (var metadata in projectItemDefinition.Metadata)
            {
                textWriter.WriteLine($"public string {metadata.Name} {{ get; set; }} = \"{metadata.EvaluatedValue}");
            }
        }

        textWriter.WriteLine($"}}");
        textWriter.WriteLine();
    }

    foreach (var (targetName, target) in tutorialProject.Targets)
    {
        textWriter.WriteLine($"void {targetName}()");
        textWriter.WriteLine($"{{");
        using (var _ = new IndentationScope(textWriter))
        {
            if (!string.IsNullOrWhiteSpace(target.Condition))
            {
                textWriter.WriteLine($"// if ({target.Condition})");
                textWriter.WriteLine($"if ({tutorialProject.ExpandString(target.Condition)}) {{ {targetName}Run = true; return; }}");
            }
            if (!string.IsNullOrWhiteSpace(target.DependsOnTargets))
            {
                textWriter.WriteLine($"// DependsOnTargets;");
                foreach (var d in tutorialProject.ExpandString(target.DependsOnTargets).Split(';'))
                {
                    textWriter.WriteLine($"if (!{d.Trim()}Run) {d.Trim()}();");
                }
            }

            if (beforeTargets.TryGetValue(targetName, out var beforeDependencies))
            {
                textWriter.WriteLine($"// BeforeTargets;");
                foreach (var d in beforeDependencies)
                {
                    textWriter.WriteLine($"if (!{d}Run) {d}();");
                }
            }

            textWriter.WriteLine();
            foreach (var child in target.Children)
            {
                if (child is ProjectPropertyGroupTaskInstance propertyGroup)
                {
                    WriteConditioned(propertyGroup.Condition, () =>
                    {
                        foreach (var property in propertyGroup.Properties)
                        {
                            WriteConditioned(property.Condition, () =>
                            {
                                textWriter.WriteLine($"/*{property.Name} = {GetValue(property.Value)};*/");
                                textWriter.WriteLine($"{property.Name} = {GetValue(ExpandString(property.Value))};");
                            });
                        }
                    });
                }
            }
            foreach (var task in target.Tasks)
            {
                // Pass parameters in the alphabet order.
                // otherwise generated code would be not diffable.
                var originalParameters = string.Join(", ", task.Parameters.OrderBy(_ => _.Key).Select((pair) => $"{pair.Key}: {GetValue(pair.Value)}"));
                var expandedParameters = string.Join(", ", task.Parameters.OrderBy(_ => _.Key).Select((pair) =>
                {
                    return $"{pair.Key}: {GetValue(ExpandString(pair.Value))}";
                }));
                if (originalParameters != expandedParameters)
                {
                    textWriter.WriteLine($"/*{task.Name}({originalParameters});*/");
                }

                WriteConditioned(task.Condition, () => textWriter.WriteLine($"{task.Name}({expandedParameters});"));
            }

            if (target.Tasks.Count > 0)
            {
                textWriter.WriteLine();
            }

            if (afterTargets.TryGetValue(targetName, out var afterDependencies))
            {
                textWriter.WriteLine($"// AfterTargets;");
                foreach (var d in afterDependencies)
                {
                    textWriter.WriteLine($"if (!{d}Run) {d}();");
                }
            }
            textWriter.WriteLine($"{targetName}Run = true;");
        }

        textWriter.WriteLine($"}}");
        textWriter.WriteLine();
    }

    var defaultTarget = tutorialProject.Properties.Single(_ => _.Name == "MSBuildProjectDefaultTargets");
    textWriter.WriteLine($"{defaultTarget.EvaluatedValue}();");
    string ExpandString(string value)
    {
        try
        {
            return tutorialProject.ExpandString(value);
        }
        catch
        {
            return value;
        }
    }

    void WriteConditioned(string condition, Action action)
    {
        if (!string.IsNullOrWhiteSpace(condition))
        {
            textWriter.WriteLine($"/* if ({condition})*/");
            string evaluatedCondition;
            try
            {
                evaluatedCondition = tutorialProject.ExpandString(condition);
            }
            catch
            {
                evaluatedCondition = condition;
            }
            textWriter.WriteLine($"if ({evaluatedCondition})");
            textWriter.WriteLine($"{{");
            using (var _ = new IndentationScope(textWriter))
            {
                action();
            }

            textWriter.WriteLine($"}}");
        }
        else
        {
            action();
        }
    }
}


class IndentationScope : IDisposable
{
    private readonly IndentedTextWriter textWriter;

    public IndentationScope(IndentedTextWriter textWriter)
    {
        this.textWriter = textWriter;
        this.textWriter.Indent++;
    }

    public void Dispose()
    {
        this.textWriter.Indent--;
    }
}