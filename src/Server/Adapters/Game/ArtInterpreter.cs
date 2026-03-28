using System.Text;
using Dna.Knowledge;

namespace Dna.Adapters.Game;

public class ArtInterpreter : IContextInterpreter
{
    public string RoleId => "art";

    public Dictionary<string, string> GetTemplates()
    {
        return new Dictionary<string, string>
        {
            [Dna.Memory.Models.WellKnownTags.Identity] = """
                {
                  "summary": "这个美术模块包含哪些资产？",
                  "contract": "骨骼标准、挂载点规范、材质通道约定",
                  "keywords": ["fbx", "texture", "rig"],
                  "description": "详细的资产规范和导出要求"
                }
                """
        };
    }

    public string InterpretContext(ModuleContext ctx)
    {
        if (ctx.IsBlocked)
            return ctx.BlockMessage ?? $"[拦截] 无权访问美术模块 '{ctx.ModuleName}'。";

        var sb = new StringBuilder();
        sb.AppendLine($"# 【美术模块】上下文 — {ctx.ModuleName}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(ctx.Summary))
            sb.AppendLine($"**职责**: {ctx.Summary}\n");
        if (ctx.Constraints is { Count: > 0 })
        {
            sb.AppendLine("**约束规则**:");
            foreach (var c in ctx.Constraints) sb.AppendLine($"- {c}");
            sb.AppendLine();
        }
        if (ctx.Metadata is { Count: > 0 })
        {
            sb.AppendLine("**扩展属性**:");
            foreach (var (k, v) in ctx.Metadata) sb.AppendLine($"- {k}: {v}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(ctx.IdentityContent))
        {
            sb.AppendLine("## 资产规范与清单 (identity)");
            sb.AppendLine(ctx.IdentityContent);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(ctx.LinksContent))
        {
            sb.AppendLine("## 跨角色交付 (links)");
            sb.AppendLine(ctx.LinksContent);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public AdapterValidationResult ValidateContext(ModuleContext ctx)
        => AdapterValidationResult.Success();
}
