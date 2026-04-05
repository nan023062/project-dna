using Dna.Knowledge.Workspace.Models;

namespace Dna.Knowledge;

public interface ITopoGraphApplicationService
{
    TopologySnapshot BuildTopology();
    TopologySnapshot GetTopology();
    TopologyWorkbenchSnapshot GetWorkbenchSnapshot();
    McdpProjectGraph GetMcdpProjection(string? projectRoot = null);
    TopologyManagementSnapshot GetManagementSnapshot();
    TopologyModuleKnowledgeView? GetModuleKnowledge(string nodeIdOrName);
    IReadOnlyList<TopologyModuleKnowledgeView> ListModuleKnowledge();
    TopologyModuleKnowledgeView SaveModuleKnowledge(TopologyModuleKnowledgeUpsertCommand command);
    TopologyModuleRelationsView? GetModuleRelations(string nodeIdOrName);
    ExecutionPlan GetExecutionPlan(List<string> moduleNames);
    KnowledgeNode? FindModule(string nameOrPath);
    List<KnowledgeNode> GetAllModules();
    List<KnowledgeNode> GetModulesByDiscipline(string disciplineId);
    ModuleContext GetModuleContext(string targetModule, string? currentModule, List<string>? activeModules = null);
    GovernanceReport ValidateArchitecture();
    List<CrossWork> GetCrossWorks();
    List<CrossWork> GetCrossWorksForModule(string moduleName);
    void RegisterModule(string discipline, TopologyModuleDefinition module);
    bool UnregisterModule(string name);
    void SaveCrossWork(TopologyCrossWorkDefinition crossWork);
    bool RemoveCrossWork(string crossWorkId);
    void UpsertDiscipline(string disciplineId, string? displayName, string roleId, List<LayerDefinition> layers);
    bool RemoveDiscipline(string disciplineId);
    string? GetDisciplineRoleId(string moduleName);
    WorkspaceTopologyContext GetWorkspaceContext();
    void ReloadManifests();
    void Initialize(string storePath);
}
