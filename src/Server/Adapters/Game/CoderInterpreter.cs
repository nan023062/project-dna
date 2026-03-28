using System.Text;
using Dna.Knowledge;

namespace Dna.Adapters.Game;

public class CoderInterpreter : IContextInterpreter
{
    public string RoleId => "coder";

    public Dictionary<string, string> GetTemplates()
    {
        return new Dictionary<string, string>
        {
            [Dna.Memory.Models.WellKnownTags.Identity] = """
                {
                  "summary": "这个模块是什么？解决什么核心问题？",
                  "contract": "对外暴露的核心接口或事件",
                  "keywords": ["csharp", "backend"],
                  "description": "详细的职责描述和边界说明"
                }
                """
        };
    }

    public string InterpretContext(ModuleContext ctx)
    {
        if (ctx.IsBlocked)
            return ctx.BlockMessage ?? $"[拦截] 无权访问模块 '{ctx.ModuleName}'。";

        var sb = new StringBuilder();
        sb.AppendLine($"# 【{ctx.Discipline ?? "generic"}】模块上下文 — {ctx.ModuleName}");
        sb.AppendLine($"- 视界: {ctx.Level}");
        sb.AppendLine();

        if (ctx.Level is ContextLevel.CrossWorkPeer)
        {
            var label = "CrossWork Contract";
            sb.AppendLine($"## {label}");
            sb.AppendLine(ctx.ContractContent ?? "（未定义 Contract）");
            return sb.ToString();
        }

        if (!string.IsNullOrWhiteSpace(ctx.Summary))
            sb.AppendLine($"**职责**: {ctx.Summary}\n");
        if (!string.IsNullOrWhiteSpace(ctx.Boundary))
            sb.AppendLine($"**边界**: {ctx.Boundary}\n");
        if (ctx.PublicApi is { Count: > 0 })
            sb.AppendLine($"**对外接口**: {string.Join(", ", ctx.PublicApi)}\n");
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
            sb.AppendLine("## 模块身份 (identity)");
            sb.AppendLine(ctx.IdentityContent);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(ctx.LinksContent))
        {
            sb.AppendLine("---");
            sb.AppendLine("## 架构依赖 (links)");
            sb.AppendLine(ctx.LinksContent);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(ctx.LessonsContent))
        {
            sb.AppendLine("---");
            sb.AppendLine("## 历史踩坑 (lessons)");
            sb.AppendLine(ctx.LessonsContent);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(ctx.ActiveContent))
        {
            sb.AppendLine("---");
            sb.AppendLine("## 当前开发任务 (active)");
            sb.AppendLine(ctx.ActiveContent);
            sb.AppendLine();
        }

        if (ctx.ContentFilePaths.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine($"## 可访问的源码文件（共 {ctx.ContentFilePaths.Count} 个）");
            foreach (var path in ctx.ContentFilePaths)
                sb.AppendLine($"- {path}");
        }

        return sb.ToString();
    }

    public AdapterValidationResult ValidateContext(ModuleContext ctx)
        => AdapterValidationResult.Success();
}
