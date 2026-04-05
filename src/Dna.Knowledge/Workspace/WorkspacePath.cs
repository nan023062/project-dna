namespace Dna.Knowledge.Workspace;

internal static class WorkspacePath
{
    public static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path
            .Replace('\\', WorkspaceConstants.Paths.RelativeSeparator)
            .Trim()
            .Trim(WorkspaceConstants.Paths.RelativeSeparator);
    }

    public static string Combine(string rootPath, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        return normalized.Length == 0
            ? Path.GetFullPath(rootPath)
            : Path.Combine(
                Path.GetFullPath(rootPath),
                normalized.Replace(WorkspaceConstants.Paths.RelativeSeparator, Path.DirectorySeparatorChar));
    }

    public static string ResolveFullPathWithinRoot(string projectRoot, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Combine(normalizedRoot, relativePath));

        if (string.Equals(candidate, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return candidate;

        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(WorkspaceConstants.Diagnostics.PathEscapesProjectRootPrefix + relativePath);

        return candidate;
    }

    public static string GetParentPath(string? relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (normalized.Length == 0)
            return string.Empty;

        var lastSlash = normalized.LastIndexOf(WorkspaceConstants.Paths.RelativeSeparator);
        return lastSlash < 0 ? string.Empty : normalized[..lastSlash];
    }

    public static bool IsSameOrDescendantOf(string scopePath, string candidatePath)
    {
        var normalizedScope = NormalizeRelativePath(scopePath);
        var normalizedCandidate = NormalizeRelativePath(candidatePath);

        if (normalizedScope.Length == 0 || normalizedCandidate.Length == 0)
            return false;

        if (string.Equals(normalizedScope, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            return true;

        return normalizedCandidate.Length > normalizedScope.Length &&
               normalizedCandidate.StartsWith(normalizedScope, StringComparison.OrdinalIgnoreCase) &&
               normalizedCandidate[normalizedScope.Length] == WorkspaceConstants.Paths.RelativeSeparator;
    }

    public static IEnumerable<string> EnumerateSegments(string? relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        return normalized.Length == 0
            ? []
            : normalized.Split(
                WorkspaceConstants.Paths.RelativeSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static string GetRelativePath(string projectRoot, string fullPath)
    {
        var normalizedRoot = Path.GetFullPath(projectRoot);
        var normalizedFullPath = Path.GetFullPath(fullPath);
        var relative = Path.GetRelativePath(normalizedRoot, normalizedFullPath);
        return NormalizeRelativePath(relative);
    }
}
