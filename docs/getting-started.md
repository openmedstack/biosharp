# Getting Started

## Introduction

Before any bioinformatics analysis can begin, the BioSharp library must be configured in your .NET project. This 
section covers the two ways to add the library as a dependency.

Adding BioSharp to your project is the essential first step — without it, none of the downstream analysis tools 
(alignment, variant calling, De Bruijn graphs, annotation) are available. Whether you reference individual projects for 
fine-grained control or the full solution for completeness, this setup enables every subsequent bioinformatics process 
described throughout this documentation.

After getting started, proceed to the [I/O Ecosystem](io-ecosystem.md) to load your first genomic data files, then follow the E2E 
pipeline sections for complete analysis workflows.

Add a project reference in your `.csproj`:

```xml
<ProjectReference Include="src/openmedstack.biosharp.model/openmedstack.biosharp.model.csproj" />
<ProjectReference Include="src/openmedstack.biosharp.io/openmedstack.biosharp.io.csproj" />
<ProjectReference Include="src/openmedstack.biosharp.calculations/openmedstack.biosharp.calculations.csproj" />
```

Or reference the solution directly:

```bash
dotnet build openmedstack-biosharp.sln
```

All namespaces are under `OpenMedStack.BioSharp`. The three top-level namespaces are:

- `OpenMedStack.BioSharp.Model` — domain models
- `OpenMedStack.BioSharp.Io` — file I/O
- `OpenMedStack.BioSharp.Calculations` — algorithms (alignment, De Bruijn graphs, variant calling, annotation)
