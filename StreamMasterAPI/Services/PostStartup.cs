﻿using MediatR;

using StreamMasterApplication.Services;

namespace StreamMasterAPI.Services;

public class PostStartup : BackgroundService
{
    private readonly ILogger _logger;
    private readonly ISender _sender;
    private readonly IBackgroundTaskQueue _taskQueue;

    public PostStartup(
        ILogger<PostStartup> logger,
        ISender sender,
        IBackgroundTaskQueue taskQueue
        )
    {
        (_logger, _taskQueue, _sender) = (logger, taskQueue, sender);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _logger.LogInformation(
        $"{nameof(PostStartup)} is running.");

        //await _hubContext.Clients.All.SystemStatusUpdate(new StreamMasterApplication.Settings.Queries.SystemStatus { IsSystemReady = false }).ConfigureAwait(false);

        // await _taskQueue.ScanDirectoryForIconFiles(cancellationToken).ConfigureAwait(false);

        //_sender.Send(new StreamMasterApplication.Settings.Queries.SystemStatus { IsSystemReady = false }, cancellationToken);

        //await _taskQueue.ReadDirectoryLogosRequest(cancellationToken).ConfigureAwait(false);

        await _taskQueue.ScanDirectoryForEPGFiles(cancellationToken).ConfigureAwait(false);

        await _taskQueue.ScanDirectoryForM3UFiles(cancellationToken).ConfigureAwait(false);

        await _taskQueue.ProcessM3UFiles(cancellationToken).ConfigureAwait(false);

        while (!_taskQueue.IsCurrent())
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        await _taskQueue.SetIsSystemReady(true, cancellationToken).ConfigureAwait(false);

        //await _taskQueue.CacheAllIcons(cancellationToken).ConfigureAwait(false);
    }
}
