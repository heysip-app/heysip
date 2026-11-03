using HeySip.Core.Features.Control;
using Microsoft.Extensions.Hosting;

namespace HeySip.App.Features.Control;

/// <summary>Starts the quiet background check for the control file. It runs beside the reminders and can never hold them up.</summary>
public sealed class ControlHostedService(ControlService control) : IHostedService
{
    private CancellationTokenSource? _stopping;
    private Task? _loop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        control.Load();
        _stopping = new CancellationTokenSource();
        _loop = Task.Run(() => control.RunAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping?.Cancel();
        if (_loop is not null)
        {
            await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
