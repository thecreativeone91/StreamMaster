﻿using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using StreamMaster.Domain.Cache;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace StreamMaster.Streams.Streams;


/// <summary>
/// Manages the streaming of a single video stream, including client registrations and circularRingbuffer handling.
/// </summary>
public sealed class StreamHandler(VideoStreamDto videoStreamDto, int processId, IMemoryCache memoryCache, IClientStreamerManager clientStreamerManager, ILogger<IStreamHandler> logger, ICircularRingBuffer ringBuffer) : IStreamHandler
{
    public static int ChunkSize = 64 * 1024;

    private readonly SemaphoreSlim getVideoInfo = new(1);
    private bool runningGetVideo { get; set; } = false;

    public event EventHandler<StreamHandlerStopped> OnStreamingStoppedEvent;

    private readonly ConcurrentDictionary<Guid, Guid> clientStreamerIds = new();

    public VideoStreamDto VideoStreamDto { get; } = videoStreamDto;
    public int M3UFileId { get; } = videoStreamDto.M3UFileId;
    public int ProcessId { get; set; } = processId;
    public ICircularRingBuffer CircularRingBuffer { get; } = ringBuffer;
    public string StreamUrl { get; } = videoStreamDto.User_Url;
    public string VideoStreamId { get; } = videoStreamDto.Id;
    public string VideoStreamName { get; } = videoStreamDto.User_Tvg_name;

    private VideoInfo? _videoInfo = null;
    private CancellationTokenSource VideoStreamingCancellationToken { get; set; } = new();

    public int ClientCount => clientStreamerIds.Count;

    public bool IsFailed { get; private set; }
    public int RestartCount { get; set; }

    private void OnStreamingStopped(bool InputStreamError)
    {
        OnStreamingStoppedEvent?.Invoke(this, new StreamHandlerStopped { StreamUrl = StreamUrl, InputStreamError = InputStreamError });
    }

    public VideoInfo GetVideoInfo()
    {
        return _videoInfo ?? new();
    }

    private async Task BuildVideoInfo()
    {
        try
        {

            if (runningGetVideo)
            {
                return;
            }

            if (_videoInfo != null)
            {
                return;
            }

            if (GetVideoInfoErrors > 3)
            {
                return;
            }

            await getVideoInfo.WaitAsync();
            runningGetVideo = true;
            ++GetVideoInfoErrors;

            Setting settings = memoryCache.GetSetting();

            string ffprobeExec = Path.Combine(BuildInfo.AppDataFolder, settings.FFProbeExecutable);

            if (!File.Exists(ffprobeExec) && !File.Exists(ffprobeExec + ".exe"))
            {
                if (!IsFFProbeAvailable())
                {
                    return;
                }
                ffprobeExec = "ffprobe";
            }

            try
            {
                long start = CircularRingBuffer.GetNextReadIndex() - 1000000;
                byte[] videoMemory = new byte[1 * 1024 * 1024];

                int mem = await CircularRingBuffer.ReadChunkMemory(start, videoMemory, CancellationToken.None);
                //byte[] videoMemoryArray = videoMemory.ToArray();
                VideoInfo ret = await CreateFFProbeStream(ffprobeExec, videoMemory).ConfigureAwait(false);

                logger.LogInformation("Retrieved video information for {name}", VideoStreamName);
                return;
            }
            catch (IOException ex)
            {

            }
            catch (Exception ex)
            {

            }
            finally
            {
                runningGetVideo = false;
                _ = getVideoInfo.Release();
            }

            return;
        }
        finally
        {
            runningGetVideo = false;
            _ = getVideoInfo.Release();
        }
    }

    private int GetVideoInfoErrors = 0;
    private async Task<VideoInfo> CreateFFProbeStream(string ffProbeExec, byte[] videoMemory)
    {
        using Process process = new();
        try
        {
            Setting settings = memoryCache.GetSetting();

            string options = "-loglevel error -print_format json -show_format -sexagesimal -show_streams - ";

            process.StartInfo.FileName = ffProbeExec;
            process.StartInfo.Arguments = options;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardInput = true;

            bool processStarted = process.Start();
            if (!processStarted)
            {
                logger.LogError("CreateFFProbeStream Error: Failed to start FFProbe process");
                return new();
            }

            using Timer timer = new(delegate { process.Kill(); }, null, 5000, Timeout.Infinite);

            using (Stream stdin = process.StandardInput.BaseStream)
            {
                await stdin.WriteAsync(videoMemory);
                await stdin.FlushAsync();
            }

            if (!process.WaitForExit(5000)) // 5000 ms timeout
            {
                // Handle the case where process doesn't exit in time
                logger.LogWarning("Process did not exit within the expected time.");
            }

            // Reading from the process's standard output
            string output = await process.StandardOutput.ReadToEndAsync();

            VideoInfo? videoInfo = JsonSerializer.Deserialize<VideoInfo>(output);
            if (videoInfo == null)
            {
                logger.LogError("CreateFFProbeStream Error: Failed to deserialize FFProbe output");
                return new();
            }
            _videoInfo = videoInfo;
            CircularRingBuffer.VideoInfo = videoInfo;
            return videoInfo;

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreateFFProbeStream Error: {ErrorMessage}", ex.Message);
            process.Kill();
        }
        return new();
    }

    private static bool IsFFProbeAvailable()
    {
        string command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
        ProcessStartInfo startInfo = new(command, "ffprobe")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        Process process = new()
        {
            StartInfo = startInfo
        };
        _ = process.Start();
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    //private readonly Memory<byte> videoMemory = new byte[1 * 1024 * 1024];
    private readonly bool startMemoryFilled = false;
    private bool testRan = false;

    public async Task StartVideoStreamingAsync(Stream stream)
    {

        VideoStreamingCancellationToken = new();
        CancellationTokenSource stopVideoStreamingToken = new();

        CircularRingBuffer.StopVideoStreamingToken = stopVideoStreamingToken;

        logger.LogInformation("Starting video read streaming, chunk size is {ChunkSize}, for stream: {StreamUrl} name: {name} circularRingbuffer id: {circularRingbuffer}", ChunkSize, StreamUrl, VideoStreamName, CircularRingBuffer.Id);

        Memory<byte> bufferMemory = new byte[ChunkSize];

        int startMemoryIndex = 0;
        bool inputStreamError = false;

        CancellationTokenSource linkedToken;
        CancellationTokenSource timeOutToken = new();

        if (!testRan && memoryCache.GetSetting().TestSettings.DropInputSeconds > 0)
        {
            logger.LogInformation($"Testing: Will stop stream in {memoryCache.GetSetting().TestSettings.DropInputSeconds} seconds.");
            timeOutToken.CancelAfter(memoryCache.GetSetting().TestSettings.DropInputSeconds * 1000);
            linkedToken = CancellationTokenSource.CreateLinkedTokenSource(stopVideoStreamingToken.Token, VideoStreamingCancellationToken.Token, timeOutToken.Token);
        }
        else
        {
            linkedToken = CancellationTokenSource.CreateLinkedTokenSource(stopVideoStreamingToken.Token, VideoStreamingCancellationToken.Token);
        }

        //foreach (Guid clientId in GetClientStreamerClientIds())
        //{
        //    IClientStreamerConfiguration? clientStreamerConfiguration = await clientStreamerManager.GetClientStreamerConfiguration(clientId);
        //    if (clientStreamerConfiguration != null && clientStreamerConfiguration.ReadBuffer != null)
        //    {
        //        long _lastReadIndex = CircularRingBuffer.GetNextReadIndex();
        //        //if (_lastReadIndex > StreamHandler.ChunkSize)
        //        //{
        //        //    _lastReadIndex -= StreamHandler.ChunkSize;
        //        //}
        //        clientStreamerConfiguration.ReadBuffer.SetLastIndex(_lastReadIndex);
        //    }
        //}

        int retryCount = 0;
        int maxRetries = 3;
        using (stream)
        {
            Stopwatch timeBetweenWrites = Stopwatch.StartNew(); // Initialize the stopwatch
            int bytesRead = bufferMemory.Length;
            while (!linkedToken.IsCancellationRequested && retryCount < maxRetries)
            {
                try
                {
                    await stream.ReadExactlyAsync(bufferMemory, linkedToken.Token);
                    CircularRingBuffer.WriteChunk(bufferMemory);
                    if (CircularRingBuffer.IsPaused())
                    {
                        CircularRingBuffer.UnPauseReaders();
                    }
                    timeBetweenWrites.Reset();

                    if (CircularRingBuffer.GetNextReadIndex() > 1000000)
                    {
                        //    if (startMemoryIndex < 1024 * 1024)
                        //    {

                        //        int bytesToCopy = Math.Min(videoMemory.Length - startMemoryIndex, bytesRead);

                        //        bufferMemory[..bytesToCopy].CopyTo(videoMemory[startMemoryIndex..]);

                        //        startMemoryIndex += bytesToCopy;
                        //    }
                        //    else
                        //    {
                        //        startMemoryFilled = true;

                        //    }
                        //    startMemoryIndex += bytesRead;
                        //}
                        //else
                        //{
                        if (GetVideoInfoErrors < 4 && _videoInfo == null && !runningGetVideo)
                        {
                            _ = BuildVideoInfo();//Run in background
                        }
                    }

                    if (timeBetweenWrites.ElapsedMilliseconds is > 30000 and < 60000000000000)
                    {

                        logger.LogWarning($"Input stream is slow: {VideoStreamName} {timeBetweenWrites.ElapsedMilliseconds}ms elapsed since last set.");

                        break;
                    }
                }
                catch (TaskCanceledException ex)
                {

                    logger.LogInformation("Stream requested to stop for: {StreamUrl} {name}", StreamUrl, VideoStreamName);
                    logger.LogInformation("Stream requested to stop for: {VideoStreamingCancellationToken}", VideoStreamingCancellationToken.IsCancellationRequested);
                    logger.LogInformation("Stream requested to stop for: {stopVideoStreamingToken.Token}", stopVideoStreamingToken.Token.IsCancellationRequested);
                    break;
                }
                catch (EndOfStreamException ex)
                {
                    inputStreamError = true;
                    ++retryCount;
                    logger.LogInformation("End of Stream reached for: {StreamUrl} {name}. Error: {ErrorMessage} at {Time} {test}", StreamUrl, VideoStreamName, ex.Message, DateTime.UtcNow, stream.CanRead);
                    if (!stream.CanRead)
                    {
                        break;
                    }

                }
                catch (HttpIOException ex)
                {
                    inputStreamError = true;
                    logger.LogInformation(ex, "HTTP IO for: {StreamUrl} {name}", StreamUrl, VideoStreamName);
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Stream error for: {StreamUrl} {name}", StreamUrl, VideoStreamName);
                    break;
                }
            }
        }

        CircularRingBuffer.PauseReaders();

        if (!stopVideoStreamingToken.IsCancellationRequested)
        {
            stopVideoStreamingToken.Cancel();
        }
        stream.Close();
        stream.Dispose();

        OnStreamingStopped(inputStreamError || (timeOutToken.IsCancellationRequested && !testRan));
        testRan = true;
    }

    public void Dispose()
    {
        CircularRingBuffer?.Dispose();
        clientStreamerIds.Clear();
        Stop();
        GC.SuppressFinalize(this);
    }

    public void Stop()
    {
        SetFailed();
        if (VideoStreamingCancellationToken?.IsCancellationRequested == false)
        {
            VideoStreamingCancellationToken.Cancel();
        }

        if (ProcessId > 0)
        {
            try
            {
                string? procName = CheckProcessExists();
                if (procName != null)
                {
                    Process process = Process.GetProcessById(ProcessId);
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error killing process {ProcessId}.", ProcessId);
            }
        }
        CircularRingBuffer?.Dispose();
    }

    public void RegisterClientStreamer(IClientStreamerConfiguration streamerConfiguration)
    {
        try
        {

            _ = clientStreamerIds.TryAdd(streamerConfiguration.ClientId, streamerConfiguration.ClientId);
            CircularRingBuffer.IncrementClient();

            logger.LogInformation("RegisterClientStreamer for Client ID {ClientId} to Video Stream Id {videoStreamId} {name} {RingBuffer.Id}", streamerConfiguration.ClientId, VideoStreamId, VideoStreamName, CircularRingBuffer.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error registering stream configuration for client {ClientId} {name} {RingBuffer.Id}", streamerConfiguration.ClientId, VideoStreamName, CircularRingBuffer.Id);
        }
    }


    public bool UnRegisterClientStreamer(Guid ClientId)
    {
        try
        {
            logger.LogInformation("UnRegisterClientStreamer ClientId: {ClientId} {name} {RingBuffer.Id}", ClientId, VideoStreamName, CircularRingBuffer.Id);
            bool result = clientStreamerIds.TryRemove(ClientId, out _);
            //CircularRingBuffer.UnRegisterClient(ClientId);
            CircularRingBuffer.DecrementClient();

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unregistering stream configuration for client {ClientId} {name} {RingBuffer.Id}", ClientId, VideoStreamName, CircularRingBuffer.Id);
            return false;
        }
    }

    private string? CheckProcessExists()
    {
        try
        {
            Process process = Process.GetProcessById(ProcessId);
            // logger.LogInformation($"Process with ID {processId} exists. Name: {process.ProcessName}");
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            // logger.LogWarning($"Process with ID {processId} does not exist.");
            return null;
        }
    }


    public IEnumerable<Guid> GetClientStreamerClientIds()
    {
        return clientStreamerIds.Keys;
    }


    public void SetFailed()
    {
        IsFailed = true;
    }

    public bool HasClient(Guid clientId)
    {
        return clientStreamerIds.ContainsKey(clientId);
    }
}