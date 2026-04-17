namespace VkPostman.Core.Services;

public interface ITemplateEngine
{
    string Render(string template, Dictionary<string, object> context);
    
    List<string> ExtractPlaceholders(string template);
    
    bool ValidateTemplate(string template, out List<string> errors);
}
