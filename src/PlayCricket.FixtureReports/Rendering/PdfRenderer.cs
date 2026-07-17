using Microsoft.Playwright;

namespace PlayCricket.FixtureReports.Rendering;

/// <summary>Prints self-contained HTML documents to A4 PDF via headless Chromium.</summary>
public sealed class PdfRenderer : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task InitialiseAsync()
    {
        _playwright = await Playwright.CreateAsync();
        var options = new BrowserTypeLaunchOptions { Headless = true };
        // Allow overriding the Chromium binary (e.g. distro-installed or pre-provisioned browsers)
        // instead of requiring `playwright install`.
        string? executable = Environment.GetEnvironmentVariable("CHROMIUM_EXECUTABLE");
        if (!string.IsNullOrEmpty(executable))
            options.ExecutablePath = executable;
        _browser = await _playwright.Chromium.LaunchAsync(options);
    }

    public async Task RenderAsync(string html, string outputPath)
    {
        if (_browser is null)
            throw new InvalidOperationException("Call InitialiseAsync first.");

        // A4 at 96dpi so the on-screen layout (and canvas chart sizes) match the printed page.
        var page = await _browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 794, Height = 1123 },
        });
        try
        {
            await page.SetContentAsync(html, new PageSetContentOptions { WaitUntil = WaitUntilState.Load });
            await page.WaitForFunctionAsync("() => window.__chartsReady === true");
            await page.PdfAsync(new PagePdfOptions
            {
                Path = outputPath,
                Format = "A4",
                PrintBackground = true,
                Margin = new Margin { Top = "8mm", Bottom = "8mm", Left = "9mm", Right = "9mm" },
            });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
