namespace Dna.Knowledge;

internal static class TopoGraphConstants
{
    internal static class Logging
    {
        public const string TopologySummary =
            "[TopoGraph] Topology: {Nodes} nodes, {Relations} relations, {DependencyEdges} dependency edges, {CrossWorks} crossworks";
    }

    internal static class Relations
    {
        public const string ContainmentLabel = "composition";
    }

    internal static class Metadata
    {
        public const string IdentityDescription = "identityDescription";
    }

    internal static class ExecutionPlan
    {
        public const string CycleDescriptionTemplate = "Circular dependency detected among: {0}";
    }

    internal static class Context
    {
        public const string MissingModuleTemplate = "Module '{0}' does not exist in the current topology.";
        public const string BlockedModuleTemplate = "Module '{0}' is not linked by dependencies or cross-work collaboration.";
        public const string EmptyLinksJson = "[]";
        public const string CrossWorkSectionHeadingPrefix = "## CrossWork: ";
        public const string ResponsibilityLinePrefix = "- Responsibility: ";
        public const string ContractLinePrefix = "- Contract: ";
        public const string DeliverableLinePrefix = "- Deliverable: ";
    }

    internal static class Governance
    {
        public const int KeyNodeThreshold = 5;
        public const string CycleMessageTemplate = "Cycle detected: {0} -> {1}";
        public const string CycleSuggestion = "Extract a shared contract module or replace the direct dependency with a cross-work collaboration.";
        public const string MissingParticipantTemplate = "Participant '{0}' does not exist in the topology.";
        public const string MissingContractOrDeliverableTemplate = "Participant '{0}' must declare a contract or deliverable.";
        public const string ContractValidationFailedTemplate = "Participant '{0}' contract validation failed: {1}";
        public const string DirectDependencyMessage = "Cross-work participants still have direct dependency edges. Remove the direct dependency and keep the collaboration contract.";
        public const string DeclaredOnlyTemplate = "Declared only: [{0}]";
        public const string ComputedOnlyTemplate = "Computed only: [{0}]";
        public const string DependencyDriftMessageTemplate = "Dependency drift: {0}";
        public const string SyncDependenciesSuggestion = "Sync declared Dependencies with ComputedDependencies.";
        public const string RemoveUnusedDependenciesSuggestion = "Remove unused declared dependencies.";
        public const string AddMissingDependenciesSuggestion = "Add the missing declared dependencies.";
        public const string KeyNodeWarningTemplate = "Key node: {0} is depended on by {1} modules.";
    }
}

public class RoleDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}

public class CollaborationRule
{
    public required string SourceRoleId { get; init; }
    public required string TargetRoleId { get; init; }
    public required string Relationship { get; init; }
    public string? Description { get; init; }
}

public class AdapterValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = [];

    public static AdapterValidationResult Success() => new() { IsValid = true };

    public static AdapterValidationResult Failure(IEnumerable<string> errors) =>
        new() { IsValid = false, Errors = new List<string>(errors) };
}

public interface IContextInterpreter
{
    string RoleId { get; }
    Dictionary<string, string> GetTemplates();
    string InterpretContext(ModuleContext context);
    AdapterValidationResult ValidateContext(ModuleContext context);
}

public interface IProjectAdapter
{
    string ProjectType { get; }
    IContextInterpreter GetInterpreter(string roleId);
    IEnumerable<RoleDefinition> GetRoles();
    IEnumerable<CollaborationRule> GetRoleCollaborationRules();
    List<string> GetModuleFiles(string relativePath);
    List<string> ComputeDependencies(KnowledgeNode module, List<KnowledgeNode> allModules);
    AdapterValidationResult ValidateContract(CrossWorkParticipant participant, KnowledgeNode module);
}

public interface ITopoGraphContextProvider
{
    TopoGraphIdentitySnapshot? GetIdentitySnapshot(string nodeId);
    TopoGraphContextContent GetContextContent(string nodeId);
}

public sealed class TopoGraphIdentitySnapshot
{
    public string? Summary { get; init; }
    public string? Contract { get; init; }
    public string? Description { get; init; }
    public List<string> Keywords { get; init; } = [];
}

public sealed class TopoGraphContextContent
{
    public string? IdentityContent { get; init; }
    public string? LessonsContent { get; init; }
    public string? ActiveContent { get; init; }
    public string? ContractContent { get; init; }
}

public interface ITopoGraphStore
{
    void Initialize(string storePath);
    void Reload();
    ComputedManifest GetComputedManifest();
    Dictionary<string, NodeKnowledge> LoadNodeKnowledgeMap();
    void UpsertNodeKnowledge(string nodeId, NodeKnowledge knowledge);
    List<string> ResolveNodeIdCandidates(string? nodeId, bool strict = false);
    void UpdateComputedDependencies(string moduleName, List<string> computedDependencies);
}
