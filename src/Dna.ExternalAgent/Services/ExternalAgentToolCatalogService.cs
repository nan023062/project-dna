using System.ComponentModel;
using System.Reflection;
using Dna.ExternalAgent.Contracts;
using Dna.ExternalAgent.Interfaces.Mcp;
using Dna.Workbench.Tooling;
using ModelContextProtocol.Server;

namespace Dna.ExternalAgent.Services;

internal sealed class ExternalAgentToolCatalogService : IExternalAgentToolCatalogService
{
    private static readonly Lazy<IReadOnlyList<WorkbenchToolDescriptor>> Catalog = new(BuildCatalog);

    public IReadOnlyList<WorkbenchToolDescriptor> ListTools() => Catalog.Value;

    private static IReadOnlyList<WorkbenchToolDescriptor> BuildCatalog()
    {
        var methods = typeof(ExternalAgentWorkbenchTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        var descriptors = new List<WorkbenchToolDescriptor>();
        foreach (var method in methods)
        {
            var tool = method.GetCustomAttribute<McpServerToolAttribute>();
            if (tool == null)
                continue;

            var name = string.IsNullOrWhiteSpace(tool.Name) ? method.Name : tool.Name;
            descriptors.Add(new WorkbenchToolDescriptor
            {
                Name = name,
                Group = ResolveGroup(name),
                Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description?.Trim() ?? "No description.",
                ReadOnly = tool.ReadOnly,
                Parameters = method.GetParameters()
                    .Select(parameter => new WorkbenchToolParameterDescriptor
                    {
                        Name = parameter.Name ?? "unknown",
                        Type = FormatTypeName(parameter.ParameterType),
                        Required = !parameter.IsOptional,
                        Description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description
                    })
                    .ToList()
            });
        }

        return descriptors
            .OrderBy(item => item.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveGroup(string toolName)
    {
        var prefix = toolName.Split('.', 2)[0];
        return prefix switch
        {
            "knowledge" => "Knowledge",
            "memory" => "Memory",
            "runtime" => "Runtime",
            "tasks" => "Tasks",
            "governance" => "Governance",
            _ => "General"
        };
    }

    private static string FormatTypeName(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return $"{FormatTypeName(nullable)}?";

        if (type.IsArray)
            return $"{FormatTypeName(type.GetElementType()!)}[]";

        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments().Select(FormatTypeName);
            if (genericDef == typeof(List<>))
                return $"List<{string.Join(", ", args)}>";
            if (genericDef == typeof(IReadOnlyList<>))
                return $"IReadOnlyList<{string.Join(", ", args)}>";
            if (genericDef == typeof(Task<>))
                return $"Task<{string.Join(", ", args)}>";
        }

        return type.Name switch
        {
            nameof(String) => "string",
            nameof(Boolean) => "bool",
            nameof(Int32) => "int",
            nameof(Int64) => "long",
            nameof(Double) => "double",
            nameof(Single) => "float",
            _ => type.Name
        };
    }
}
