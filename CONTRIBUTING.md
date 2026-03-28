# Contributing to Project DNA

Thank you for your interest in contributing to Project DNA!

## Ways to Contribute

### 1. DNA Templates

A template is a pre-built DNA structure for a specific project type (e.g., Unity game, React app, Spring Boot service).

**Format**: A JSON file describing the default node tree, dependencies, and knowledge.

```
templates/
└── unity-game/
    ├── dna-template.json
    └── README.md
```

### 2. Scanners

A scanner automatically analyzes project files and generates DNA nodes and edges.

Examples: C# project scanner, TypeScript import scanner, Unity Assembly Definition scanner.

### 3. Governance Rules

Custom architecture checks that plug into the GovernanceEngine.

Examples: "Unity performance rules", "Clean architecture rules", "React best practices".

### 4. Bug Reports & Feature Requests

Open an issue with:
- Clear description of the problem or feature
- Steps to reproduce (for bugs)
- Your environment (OS, .NET version, IDE)

### 5. Code Contributions

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Make your changes
4. Run `dotnet build` to verify
5. Commit with a clear message
6. Open a Pull Request

## Development Setup

```bash
# Prerequisites: .NET 10 SDK

cd src
dotnet build
dotnet run --project Server -- --project /path/to/test/project
```

## Code Style

- Follow existing C# conventions in the codebase
- Use meaningful names; avoid abbreviations
- Keep methods focused and small
- No comments that just narrate what code does

## License

By contributing, you agree that your contributions will be licensed under the Apache License 2.0.
