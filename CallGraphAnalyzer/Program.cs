using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

class CallGraphAnalyzer
{
    private static Dictionary<string, HashSet<string>> callGraph = new();
    private static HashSet<string> allMethods = new();

    static async Task Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: CallGraphAnalyzer <path-to-solution.sln> [output.dot]");
            return;
        }

        string solutionPath = args[0];
        string outputPath = args.Length > 1 ? args[1] : "callgraph.dot";

        Console.WriteLine($"Analyzing solution: {solutionPath}");
        
        // Register MSBuild
        Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();

        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (sender, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                Console.WriteLine($"Warning: {e.Diagnostic.Message}");
            }
        };

        var solution = await workspace.OpenSolutionAsync(solutionPath);
        
        Console.WriteLine($"Loaded solution with {solution.Projects.Count()} projects");

        // First pass: collect all methods
        foreach (var project in solution.Projects)
        {
            Console.WriteLine($"Processing project: {project.Name}");
            var compilation = await project.GetCompilationAsync();
            
            if (compilation == null) continue;

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var root = await syntaxTree.GetRootAsync();
                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                // Find all method declarations
                var methodDeclarations = root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>();

                foreach (var method in methodDeclarations)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(method);
                    if (symbol != null)
                    {
                        string methodName = GetMethodSignature(symbol);
                        allMethods.Add(methodName);
                        if (!callGraph.ContainsKey(methodName))
                        {
                            callGraph[methodName] = new HashSet<string>();
                        }
                    }
                }
            }
        }

        // Second pass: analyze method calls
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            
            if (compilation == null) continue;

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var root = await syntaxTree.GetRootAsync();
                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                // Find all method declarations
                var methodDeclarations = root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>();

                foreach (var method in methodDeclarations)
                {
                    var callerSymbol = semanticModel.GetDeclaredSymbol(method);
                    if (callerSymbol == null) continue;

                    string callerName = GetMethodSignature(callerSymbol);

                    // Find all invocations within this method
                    var invocations = method.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>();

                    foreach (var invocation in invocations)
                    {
                        var calleeSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                        if (calleeSymbol != null)
                        {
                            string calleeName = GetMethodSignature(calleeSymbol);
                            
                            // Only track calls within our codebase
                            if (allMethods.Contains(calleeName))
                            {
                                callGraph[callerName].Add(calleeName);
                            }
                        }
                    }
                }
            }
        }

        // Generate DOT file
        GenerateDotFile(outputPath);
        
        Console.WriteLine($"\nCall graph generated: {outputPath}");
        Console.WriteLine($"Total methods: {allMethods.Count}");
        Console.WriteLine($"Total call relationships: {callGraph.Values.Sum(v => v.Count)}");
        Console.WriteLine($"\nTo visualize, run:");
        Console.WriteLine($"  dot -Tpng {outputPath} -o callgraph.png");
        Console.WriteLine($"  dot -Tsvg {outputPath} -o callgraph.svg");
    }

    static string GetMethodSignature(IMethodSymbol method)
    {
        string className = method.ContainingType?.Name ?? "Unknown";
        string methodName = method.Name;
        
        // Simplify: just use ClassName.MethodName
        return $"{className}.{methodName}";
    }

    static void GenerateDotFile(string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph CallGraph {");
        sb.AppendLine("  rankdir=LR;");
        sb.AppendLine("  node [shape=box, style=rounded];");
        sb.AppendLine();

        // Node styling based on method characteristics
        foreach (var method in allMethods)
        {
            string nodeId = SanitizeNodeId(method);
            string label = method;
            
            // Color nodes by class
            int colorIndex = Math.Abs(method.Split('.')[0].GetHashCode()) % 12;
            string color = GetColor(colorIndex);
            
            sb.AppendLine($"  \"{nodeId}\" [label=\"{label}\", fillcolor=\"{color}\", style=\"filled,rounded\"];");
        }

        sb.AppendLine();

        // Edges
        foreach (var kvp in callGraph)
        {
            string caller = SanitizeNodeId(kvp.Key);
            foreach (var callee in kvp.Value)
            {
                string calleeId = SanitizeNodeId(callee);
                sb.AppendLine($"  \"{caller}\" -> \"{calleeId}\";");
            }
        }

        sb.AppendLine("}");

        File.WriteAllText(outputPath, sb.ToString());
    }

    static string SanitizeNodeId(string id)
    {
        return id.Replace("\"", "\\\"");
    }

    static string GetColor(int index)
    {
        string[] colors = {
            "#E8F4F8", "#FFEAA7", "#DFE6E9", "#FAB1A0", 
            "#A29BFE", "#FD79A8", "#FDCB6E", "#6C5CE7",
            "#74B9FF", "#55EFC4", "#FF7675", "#B2BEC3"
        };
        return colors[index];
    }
}
