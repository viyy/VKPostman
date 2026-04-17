using Scriban;
using Scriban.Parsing;
using Scriban.Syntax;
using VkPostman.Core.Services;

namespace VkPostman.Templates;

public class ScribanTemplateEngine : ITemplateEngine
{
    public string Render(string template, Dictionary<string, object> context)
    {
        try
        {
            var scribanTemplate = Template.Parse(template);
            if (scribanTemplate.HasErrors)
            {
                var errors = string.Join("; ", scribanTemplate.Messages.Select(m => m.Message));
                throw new InvalidOperationException($"Template parsing failed: {errors}");
            }

            var result = scribanTemplate.Render(context);
            return result;
        }
        catch (ScriptRuntimeException ex)
        {
            throw new InvalidOperationException($"Template rendering failed: {ex.Message}", ex);
        }
    }

    public List<string> ExtractPlaceholders(string template)
    {
        var placeholders = new List<string>();
        
        try
        {
            var parsed = Template.Parse(template);
            if (parsed.HasErrors)
                return placeholders;

            // Extract variable references from the AST
            ExtractVariablesFromNode(parsed.Page!, placeholders);
        }
        catch
        {
            // If parsing fails, fall back to regex
            return ExtractPlaceholdersRegex(template);
        }

        return placeholders.Distinct().ToList();
    }

    public bool ValidateTemplate(string template, out List<string> errors)
    {
        errors = new List<string>();
        
        try
        {
            var parsed = Template.Parse(template);
            if (parsed.HasErrors)
            {
                errors.AddRange(parsed.Messages.Select(m => m.Message));
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"Template validation failed: {ex.Message}");
            return false;
        }
    }

    private void ExtractVariablesFromNode(ScriptNode node, List<string> placeholders)
    {
        switch (node)
        {
            case ScriptVariableGlobal variable:
                placeholders.Add(variable.Name);
                break;
            case ScriptMemberExpression member when member.Target is ScriptVariableGlobal targetVar:
                placeholders.Add(targetVar.Name);
                break;
        }

        foreach (var child in node.Children)
        {
            ExtractVariablesFromNode(child, placeholders);
        }
    }

    private static List<string> ExtractPlaceholdersRegex(string template)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(
            template, 
            @"\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}");
            
        return matches.Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
    }
}
