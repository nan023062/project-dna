namespace Dna.Knowledge.Workspace;

internal static class WorkspaceConstants
{
    public static class Metadata
    {
        public const string FileName = ".agentic.meta";
        public const string Schema = "agentic-os/workspace-directory/v1";
    }

    public static class Paths
    {
        public const char RelativeSeparator = '/';
        public const string RelativeSeparatorText = "/";
    }

    public static class Labels
    {
        public const string Untracked = "Untracked";
        public const string RegisteredModule = "Registered module";
        public const string CrossWorkModule = "Cross-work module";
        public const string DescribedDirectory = "Directory described by .agentic.meta";
        public const string ManagedScope = "Managed scope";
        public const string TrackedByModuleScope = "Tracked by module scope";
        public const string ModuleContainer = "Module container";
        public const string CandidateDirectory = "Candidate directory";
        public const string UntrackedFile = "Untracked file";
    }

    public static class Badges
    {
        public const string RootDiscipline = "root";
        public const string ManagedSuffix = "managed";
        public const string ScopeSuffix = "scope";
        public const string CrossWorkSuffix = "cross-work";
        public const string Container = "container";
        public const string LayerPrefix = "L";
        public const string Metadata = "meta";
    }

    public static class Persona
    {
        public const string DefaultName = "Agentic OS";
        public const string DefaultRoleId = "coder";
    }

    public static class Diagnostics
    {
        public const string CacheInitialized = "WorkspaceTreeCache initialized: {Root}";
        public const string WatcherSetupFailed = "Workspace FileSystemWatcher setup failed.";
        public const string WatcherError = "Workspace FileSystemWatcher error, rebuilding watcher.";
        public const string DirectoryDoesNotExistPrefix = "Workspace directory does not exist: ";
        public const string PathEscapesProjectRootPrefix = "Workspace path escapes project root: ";
    }

    public static class Timing
    {
        public const int WatcherDebounceMilliseconds = 250;
    }

    public static class Disciplines
    {
        public const string Engineering = "engineering";
        public const string Art = "art";
        public const string Design = "design";
        public const string DevOps = "devops";
        public const string Qa = "qa";
        public const string TechSupport = "tech-support";
        public const string ProductDesign = "product-design";
    }

    public static class DisciplinePathPrefixes
    {
        public static readonly string[] Engineering = ["src/", "code/"];
        public static readonly string[] Art = ["art/", "assets/"];
        public static readonly string[] Design = ["design/"];
        public static readonly string[] DevOps = ["devops/", "ci/"];
        public static readonly string[] Qa = ["qa/", "test/", "tests/"];
        public static readonly string[] TechSupport = ["tools/"];
        public static readonly string[] ProductDesign = ["docs/"];
    }

    public static class ExcludedDirectories
    {
        public static readonly string[] Names =
        [
            ".git",
            "node_modules",
            "bin",
            "obj",
            ".dna",
            ".agentic-os",
            ".project.dna",
            ".vs",
            ".idea",
            "Temp",
            "Library",
            "Logs",
            "logs",
            "__pycache__",
            ".vscode",
            ".cursor",
            "publish",
            "Publish",
            ".gradle",
            "build",
            "dist",
            "out"
        ];
    }
}
