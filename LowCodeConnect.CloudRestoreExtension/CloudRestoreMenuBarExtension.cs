using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;

namespace LowCodeConnect.CloudRestoreExtension;


[Export(typeof(MenuExtension))]
[method: ImportingConstructor]
public class CloudRestoreMenuBarExtension(IDockingWindowService dockingWindowService) : MenuExtension
{

    public override IEnumerable<MenuViewModel> GetMenus()
    {
        yield return new MenuViewModel("Cloud database restore", () => dockingWindowService.OpenPane(CloudRestoreDockablePaneExtension.ID));

    }
}
