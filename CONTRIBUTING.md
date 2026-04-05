# Contributing to Agentic OS

Thank you for your interest in contributing!

## Bug Reports & Feature Requests

Open an issue with:
- Clear description of the problem or feature
- Steps to reproduce (for bugs)
- Your environment (OS, .NET version, IDE)

## Code Contributions

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Make your changes
4. Run `dotnet build` to verify
5. Commit with a clear message
6. Open a Pull Request

## Development Setup

```bash
# Prerequisites: .NET 10 SDK

dotnet build src/App/App.csproj
dotnet run --no-launch-profile --project src/App
```

## License

By contributing, you agree that your contributions will be licensed under the Apache License 2.0.
