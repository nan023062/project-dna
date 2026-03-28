using Dna.Knowledge.Models;
using Dna.Knowledge.Project.Models;

namespace Dna.Knowledge;

public interface IGraphEngine
{
    TopologySnapshot BuildTopology();
    TopologySnapshot GetTopology();
    ExecutionPlan GetExecutionPlan(List<string> moduleNames);

    KnowledgeNode? FindModule(string nameOrPath);
    List<KnowledgeNode> GetAllModules();
    List<KnowledgeNode> GetModulesByDiscipline(string disciplineId);
    ModuleContext GetModuleContext(string targetModule, string? currentModule, List<string>? activeModules = null);

    List<CrossWork> GetCrossWorks();
    List<CrossWork> GetCrossWorksForModule(string moduleName);

    void RegisterModule(string discipline, ModuleRegistration module);
    bool UnregisterModule(string name);

    void SaveCrossWork(CrossWorkRegistration crossWork);
    bool RemoveCrossWork(string crossWorkId);

    void UpsertDiscipline(string disciplineId, string? displayName, string roleId, List<LayerDefinition> layers);
    bool RemoveDiscipline(string disciplineId);
    string? GetDisciplineRoleId(string moduleName);

    ArchitectureManifest GetArchitecture();
    ModulesManifest GetModulesManifest();
    void ReplaceModulesManifest(ModulesManifest manifest);
    void ReloadManifests();

    void Initialize(string storePath);
}
