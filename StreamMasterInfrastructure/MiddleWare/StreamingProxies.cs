﻿using StreamMasterDomain.Common;

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StreamMasterInfrastructure.MiddleWare;

public static class StreamingProxies
{
    private static bool IsFFmpegAvailable()
    {
        string command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
        ProcessStartInfo startInfo = new ProcessStartInfo(command, "ffmpeg");
        startInfo.RedirectStandardOutput = true;
        startInfo.UseShellExecute = false;
        Process process = new Process();
        process.StartInfo = startInfo;
        process.Start();
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    /// <summary>
    /// Get FFMpeg Stream from url <strong>Supports failover</strong>
    /// </summary>
    /// <param name="streamUrl">URL to stream from</param>
    /// <param name="ffMPegExecutable">
    /// Path to FFMpeg executable. If it doesnt exist <strong>null</strong> is returned
    /// </param>
    /// <param name="user_agent">user_agent string</param>
    /// <returns><strong>A FFMpeg backed stream or null</strong></returns>
    public static async Task<(Stream? stream, ProxyStreamError? error)> GetFFMpegStream(string streamUrl, string ffMPegExecutable, string user_agent)
    {
        if (!IsFFmpegAvailable())
        {
            ProxyStreamError error = new() { ErrorCode = ProxyStreamErrorCode.FileNotFound, Message = $"FFmpeg executable file not found: {ffMPegExecutable}" };
            return (null, error);
        }

        try
        {
            using Process process = new();
            process.StartInfo.FileName = ffMPegExecutable;
            process.StartInfo.Arguments = $"-hide_banner -loglevel error -i \"{streamUrl}\" -c copy -f mpegts pipe:1 -user_agent \"{user_agent}\"";
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;

            _ = process.Start();

            return (await Task.FromResult(process.StandardOutput.BaseStream).ConfigureAwait(false), null);
        }
        catch (IOException ex)
        {
            ProxyStreamError error = new() { ErrorCode = ProxyStreamErrorCode.IoError, Message = ex.Message };
            return (null, error);
        }
        catch (Exception ex)
        {
            ProxyStreamError error = new() { ErrorCode = ProxyStreamErrorCode.UnknownError, Message = ex.Message };
            return (null, error);
        }
    }

    /// <summary>
    /// This is a pass through proxy <strong>Supports failover</strong>
    /// </summary>
    /// <param name="streamUrl">URL to stream from</param>
    /// <param name="user_agent">user_agent string</param>
    /// <returns><strong>A FFMpeg backed stream or null</strong></returns>
    public static async Task<(Stream? stream, ProxyStreamError? error)> GetProxyStream(string streamUrl)
    {
        try
        {
            using HttpClientHandler handler = new() { AllowAutoRedirect = true };
            using HttpClient httpClient = new(handler);
            string userAgentString = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36 Edg/110.0.1587.57";
            httpClient.DefaultRequestHeaders.Add("User-Agent", userAgentString);

            int redirectCount = 0;

            HttpResponseMessage response = await httpClient.GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            while (response.StatusCode == System.Net.HttpStatusCode.Redirect)
            {
                ++redirectCount;
                if (response.Headers.Location == null || redirectCount > 10)
                {
                    break;
                }

                string location = response.Headers.Location.ToString();
                response = await httpClient.GetAsync(location, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }

            string contentType = response.Content.Headers.ContentType.MediaType;
            if (contentType == "application/vnd.apple.mpegurl")
            {
                ProcessStartInfo ffmpegStartInfo = new ProcessStartInfo();
                ffmpegStartInfo.FileName = "ffmpeg";
                ffmpegStartInfo.Arguments = $"-i {streamUrl} -c copy -f mp4 pipe:1";
                ffmpegStartInfo.RedirectStandardOutput = true;
                Process ffmpegProcess = new Process();
                ffmpegProcess.StartInfo = ffmpegStartInfo;
                ffmpegProcess.Start();

                MemoryStream memoryStream = new MemoryStream();
                using (Stream ffmpegOutput = ffmpegProcess.StandardOutput.BaseStream)
                {
                    byte[] buffer = new byte[4096];
                    int bytesRead;
                    while ((bytesRead = ffmpegOutput.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        memoryStream.Write(buffer, 0, bytesRead);
                    }
                }
                memoryStream.Seek(0, SeekOrigin.Begin);
                return (memoryStream, null);
            }
            else
            {
                Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                return (stream, null);
            }
        }
        catch (HttpRequestException ex)
        {
            ProxyStreamError error = new() { ErrorCode = ProxyStreamErrorCode.HttpRequestError, Message = ex.Message };
            return (null, error);
        }
        catch (IOException ex)
        {
            ProxyStreamError error = new() { ErrorCode = ProxyStreamErrorCode.IoError, Message = ex.Message };
            return (null, error);
        }
        catch (Exception ex)
        {
            ProxyStreamError error = new() { ErrorCode = ProxyStreamErrorCode.UnknownError, Message = ex.Message };
            return (null, error);
        }
    }
}
