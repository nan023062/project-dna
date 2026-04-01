namespace Dna.Client.Interfaces.Api;

public static class ClientLocalKnowledgeEndpoints
{
    public static void MapClientLocalKnowledgeEndpoints(this WebApplication app)
    {
        app.MapClientLocalRuntimeApiEndpoints();
        app.MapClientLocalTopologyApiEndpoints();
        app.MapClientLocalKnowledgeManagementApiEndpoints();
        app.MapClientLocalMemoryApiEndpoints();
    }
}
