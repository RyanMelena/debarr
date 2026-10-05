using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

public partial class App(NavigationManager navigationManager)
{
    // UsePathBase puts the URL base into BaseUri, so the href matches the path the browser requested.
    private string BaseHref => new Uri(navigationManager.BaseUri).AbsolutePath;
}
