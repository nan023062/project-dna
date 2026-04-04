namespace Dna.App.Interfaces.Api;

public static class AppLocalKnowledgeEndpoints
{
    public static void MapAppLocalKnowledgeEndpoints(this WebApplication app)
    {
        app.MapAppLocalRuntimeApiEndpoints();
        app.MapAppLocalKnowledgeManagementApiEndpoints();
    }
}
