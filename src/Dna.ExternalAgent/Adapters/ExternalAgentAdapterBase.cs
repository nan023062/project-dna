using System.Text;
using Dna.ExternalAgent.Contracts;
using Dna.ExternalAgent.Models;

namespace Dna.ExternalAgent.Adapters;

internal abstract class ExternalAgentAdapterBase : IExternalAgentAdapter
{
    public abstract ExternalAgentAdapterDescriptor Descriptor { get; }

    public abstract ExternalAgentPackageResult BuildPackage(ExternalAgentPackageContext context);

    protected static string BuildToolListMarkdown(ExternalAgentPackageContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Required Agentic OS Tools");
        builder.AppendLine();

        foreach (var tool in context.RequiredTools)
            builder.AppendLine($"- `{tool.Name}`: {tool.Description}");

        return builder.ToString().TrimEnd();
    }
}
