


using System.ComponentModel.Composition;
using System.Net;
using global::Mendix.StudioPro.ExtensionsAPI.Services;
using global::Mendix.StudioPro.ExtensionsAPI.UI.WebServer;


namespace LowCodeConnect.CloudRestoreExtension;

[Export(typeof(WebServerExtension))]
public class CloudRestoreWebServerExtension : WebServerExtension
{
    private readonly IExtensionFileService _extensionFileService;
    private readonly ILogService _logService;

    [ImportingConstructor]
    public CloudRestoreWebServerExtension(IExtensionFileService extensionFileService, ILogService logService)
    {
        _extensionFileService = extensionFileService;
        _logService = logService;
    }

    public override void InitializeWebServer(IWebServer webServer)
    {
        webServer.AddRoute("index", ServeIndex);
        webServer.AddRoute("pico.classless.blue.css", ServePicoCSS);
        webServer.AddRoute("settings.html", ServeSettings);
        webServer.AddRoute("style.css", ServeStyleCSS);
    }

    private async Task ServeIndex(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
    {
        var indexFilePath = _extensionFileService.ResolvePath("wwwroot", "index.html");
        await response.SendFileAndClose("text/html", indexFilePath, ct);
    }

    private async Task ServePicoCSS(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
    {
        var indexFilePath = _extensionFileService.ResolvePath("wwwroot", "pico.classless.blue.css");
        await response.SendFileAndClose("text/css", indexFilePath, ct);
    }

    private async Task ServeSettings(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
    {
        var indexFilePath = _extensionFileService.ResolvePath("wwwroot", "settings.html");
        await response.SendFileAndClose("text/html", indexFilePath, ct);
    }

    private async Task ServeStyleCSS(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
    {
        var indexFilePath = _extensionFileService.ResolvePath("wwwroot", "style.css");
        await response.SendFileAndClose("text/css", indexFilePath, ct);
    }




}