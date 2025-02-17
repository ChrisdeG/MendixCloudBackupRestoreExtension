
using System.ComponentModel.Composition;
using global::Mendix.StudioPro.ExtensionsAPI.Services;
using global::Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;


namespace LowCodeConnect.CloudRestoreExtension;

[Export(typeof(DockablePaneExtension))]
public class CloudRestoreDockablePaneExtension : DockablePaneExtension
{
    private readonly ILogService _logService;
    private readonly IHttpClientService _httpClientService;
    private readonly IConfigurationService _configurationService;
    public const string ID = "CloudBackupRestore";
    public override string Id => ID;

    [ImportingConstructor]
    public CloudRestoreDockablePaneExtension(ILogService logService, IHttpClientService httpClientService, IConfigurationService configurationService
        )
    {
        _logService = logService;
        _httpClientService = httpClientService;
        _configurationService = configurationService;
        _logService.Info(message: "Cloud database restore started");
    }

    public override DockablePaneViewModelBase Open()
    {
        return new CloudRestoreDockablePaneViewModel(WebServerBaseUrl, () => CurrentApp, _logService, _httpClientService, _configurationService) { Title = "Cloud database restore" };
    }
}