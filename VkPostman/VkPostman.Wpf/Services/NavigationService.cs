namespace VkPostman.Wpf.Services;

/// <summary>
/// Tiny pub/sub bridge for cross-view navigation. Registered as a singleton;
/// view-models raise requests and the shell (MainViewModel) reacts. Keeps the
/// Drafts VM from needing a direct reference to the shell or the Templates VM.
/// </summary>
public class NavigationService
{
    /// <summary>Raised when something asks to open a specific template by id.</summary>
    public event Action<int>? TemplateRequested;

    public void RequestTemplate(int templateId) => TemplateRequested?.Invoke(templateId);
}
