using Debarr.Appearance;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Debarr.Components.Layout;

public partial class MainLayout
{
    private MudThemeProvider? _themeProvider;

    // Dark until the browser reports its preference, which it can only do once the circuit renders.
    private bool _systemIsDark = true;
    private bool _drawerOpen = true;
    private ErrorBoundary? _errorBoundary;
    private ShowAdvanced _showAdvanced;

    public MainLayout() => _showAdvanced = new ShowAdvanced(false, ToggleShowAdvanced);

    [CascadingParameter]
    private UITheme Theme { get; set; }

    private bool IsDarkMode => Theme switch
    {
        UITheme.Light => false,
        UITheme.Dark => true,
        _ => _systemIsDark,
    };

    // A navigation to another page clears the error the last page showed.
    protected override void OnParametersSet() => _errorBoundary?.Recover();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _themeProvider is not null)
        {
            _systemIsDark = await _themeProvider.GetSystemDarkModeAsync();
            await _themeProvider.WatchSystemDarkModeAsync(SetSystemIsDarkAsync);
            StateHasChanged();
        }
    }

    private Task SetSystemIsDarkAsync(bool isDark)
    {
        _systemIsDark = isDark;
        return InvokeAsync(StateHasChanged);
    }

    private void Recover() => _errorBoundary?.Recover();

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;

    private void OpenDrawer() => _drawerOpen = true;

    // The button that calls this renders inside a page or a modal, so the layout renders itself to cascade the new value.
    private void ToggleShowAdvanced()
    {
        _showAdvanced = _showAdvanced with { Shown = !_showAdvanced.Shown };
        StateHasChanged();
    }
}
