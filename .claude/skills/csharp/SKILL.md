---
name: csharp
description: Use this skill when editing, creating, or reviewing any C# file (.cs) in this repo. Covers naming conventions, using directive rules, and project structure for Covenant feature domains.
---

# C# Coding Conventions

## Naming

Never abbreviate variable, parameter, field, or property names. Always use the full word.

```csharp
// Bad
CancellationToken ct
CovenantDbContext ctx
CovenantDbContext db
IServiceProvider sp
DbContextOptionsBuilder b

// Good
CancellationToken cancellationToken
CovenantDbContext dbContext
IServiceProvider serviceProvider
DbContextOptionsBuilder optionsBuilder
```

Domain acronyms that are proper nouns or file-extension identifiers are allowed: `DOCX`, `NDA`, `MSA`, `DPA`, `SOW`. The framework entry-point parameter `string[] args` is also allowed.

## Using directives

Always resolve types with `using` directives at the top of the file. Never qualify a type with its full namespace inline (e.g. write `Result<Guid>`, not `Covenant.Domain.Result<Guid>`). If a name collision exists, use an alias (`using DomainResult = Covenant.Domain.Result<Guid>`) rather than inline qualification.

## Project structure

Feature domain projects (`Covenant.{Feature}`) are organised into sub-folders:

```
Covenant.{Feature}/
  Entities/    – domain entities and value objects
  Models/      – commands, queries, and DTOs
  Services/    – service interfaces
```

Add new sub-folders only when a clear grouping emerges. Do not create a flat project with all files at the root once any sub-folder exists.
