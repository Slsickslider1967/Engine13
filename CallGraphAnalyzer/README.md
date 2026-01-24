# Call Graph Analyzer

A Roslyn-based tool that analyzes C# code and generates call graphs using Graphviz.

## Features

- Analyzes entire .NET solutions using Roslyn
- Generates DOT format files for Graphviz
- Color-codes nodes by class
- Tracks method-to-method call relationships
- Filters to show only internal method calls (excludes external library calls)

## Usage

```bash
# Analyze a solution and generate call graph
dotnet run --project CallGraphAnalyzer.csproj <path-to-solution.sln> [output.dot]

# Example:
dotnet run --project CallGraphAnalyzer.csproj ../Engine13/Engine-13.sln callgraph.dot
```

## Visualization

After generating the DOT file, use Graphviz to create visual representations:

```bash
# Generate PNG
dot -Tpng callgraph.dot -o callgraph.png

# Generate SVG (scalable, better for large graphs)
dot -Tsvg callgraph.dot -o callgraph.svg

# Generate PDF
dot -Tpdf callgraph.dot -o callgraph.pdf
```

## Output

The analyzer outputs:
- **callgraph.dot** - DOT format file (42KB)
- **callgraph.png** - PNG visualization (2.5MB)
- **callgraph.svg** - SVG visualization (275KB)

### Statistics from Engine-13:
- Total methods: 249
- Total call relationships: 265

## Requirements

- .NET 10.0 SDK
- Graphviz (for visualization)

### Installing Graphviz

**Arch Linux:**
```bash
sudo pacman -S graphviz
```

**Ubuntu/Debian:**
```bash
sudo apt-get install graphviz
```

**macOS:**
```bash
brew install graphviz
```

## How it Works

1. Uses Roslyn's MSBuildWorkspace to load the solution
2. First pass: Collects all method declarations
3. Second pass: Analyzes method invocations within each method
4. Generates DOT format with color-coded nodes
5. Graphviz renders the final visualization

## Customization

Edit `Program.cs` to customize:
- Node colors and styles
- Graph layout (rankdir, node shape)
- Filtering criteria
- Method signature format
