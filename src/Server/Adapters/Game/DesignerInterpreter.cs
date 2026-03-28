using System.Text;
using Dna.Knowledge;

namespace Dna.Adapters.Game;

public class DesignerInterpreter : IContextInterpreter
{
    public string RoleId => "designer";

    public Dictionary<string, string> GetTemplates()
    {
        return new Dictionary<string, string>
        {
            [Dna.Memory.Models.WellKnownTags.Identity] = """
                {
                  "summary": "这个策划模块负责什么系统的设计？",
                  "contract": "数据外键约定、数值公式参数、Tags 枚举",
                  "keywords": ["json", "excel", "balance"],
                  "description": "详细的系统设计意图和数值边界"
                }
                """
        };
    }

    public string InterpretContext(ModuleContext ctx)
    {
        if (ctx.IsBlocked)
            return ctx.BlockMessage ?? $"[拦截] 无权访问策划模块 '{ctx.ModuleName}'。";

        var sb = new StringBuilder();
        sb.AppendLine($"# 【策划模块】上下文 — {ctx.ModuleName}");
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
            sb.AppendLine("## 设计规则与配置 (identity)");
            sb.AppendLine(ctx.IdentityContent);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(ctx.LinksContent))
        {
            sb.AppendLine("## 跨角色协作 (links)");
            sb.AppendLine(ctx.LinksContent);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(ctx.ActiveContent))
        {
            sb.AppendLine("## 当前策划任务 (active)");
            sb.AppendLine(ctx.ActiveContent);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public AdapterValidationResult ValidateContext(ModuleContext ctx)
        => AdapterValidationResult.Success();
}
