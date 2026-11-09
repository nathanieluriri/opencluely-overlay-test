using System.Text;
using System.Threading.Channels;
using NAudio.Wave;
using OverlayPoc.Models;
using Whisper.net;
using Whisper.net.Ggml;

namespace OverlayPoc.Services;

internal sealed class SpeechInputService : IAsyncDisposable
{
    private const int SampleRate = 16000;
    private const int ChunkDurationSeconds = 4;
    private const int ChunkByteCount = SampleRate * sizeof(short) * ChunkDurationSeconds;
    private static readonly string ModelDirectory = Path.Combine(LlmConfigLoader.DirectoryPath, "models");
    private static readonly string ModelPath = Path.Combine(ModelDirectory, "ggml-tiny.bin");

    private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly List<byte> _pendingAudio = new();
    private readonly object _audioLock = new();
    private readonly TaskCompletionSource _recordingStopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private WaveInEvent _capture;
    private WhisperFactory _factory;
    private WhisperProcessor _processor;
    private Task _processingTask;
    private Task<string> _stopTask;
    private string _transcript = "";
    private bool _started;

    public event Action<string> TranscriptUpdated;

    public async Task StartAsync(Action<string> statusChanged)
    {
        await EnsureModelAsync(statusChanged);

        _factory = WhisperFactory.FromPath(ModelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage("auto")
            .Build();
        _processingTask = Task.Run(ProcessChunksAsync);

        _capture = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 100
        };
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        _started = true;
    }

    public Task<string> StopAsync()
    {
        _stopTask ??= StopCoreAsync();
        return _stopTask;
    }

    private async Task<string> StopCoreAsync()
    {
        Exception stopError = null;
        try
        {
            if (_started)
            {
                _capture.StopRecording();
                await _recordingStopped.Task;
            }
        }
        catch (Exception ex)
        {
            stopError = ex;
        }
        finally
        {
            lock (_audioLock)
            {
                if (_pendingAudio.Count > 0)
                {
                    _chunks.Writer.TryWrite(_pendingAudio.ToArray());
                    _pendingAudio.Clear();
                }
            }

            _chunks.Writer.TryComplete();
        }

        try
        {
            if (_processingTask is not null)
                await _processingTask;
        }
        catch (Exception ex)
        {
            stopError ??= ex;
        }

        if (stopError is not null)
            throw new InvalidOperationException("Speech capture or transcription failed.", stopError);

        return _transcript;
    }

    private async Task EnsureModelAsync(Action<string> statusChanged)
    {
        if (File.Exists(ModelPath))
            return;

        Directory.CreateDirectory(ModelDirectory);
        var temporaryPath = ModelPath + ".download";
        statusChanged("Downloading local Whisper model (first use)...");
        Program.Log("Downloading Whisper tiny model");

        try
        {
            using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Tiny);
            await using (var fileStream = new FileStream(
                temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await modelStream.CopyToAsync(fileStream);
            }

            File.Move(temporaryPath, ModelPath, overwrite: true);
            Program.Log("Whisper tiny model downloaded");
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }
    }

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        lock (_audioLock)
        {
            for (var i = 0; i < e.BytesRecorded; i++)
                _pendingAudio.Add(e.Buffer[i]);

            while (_pendingAudio.Count >= ChunkByteCount)
            {
                var chunk = _pendingAudio.GetRange(0, ChunkByteCount).ToArray();
                _pendingAudio.RemoveRange(0, ChunkByteCount);
                _chunks.Writer.TryWrite(chunk);
            }
        }
    }

    private void OnRecordingStopped(object sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            _recordingStopped.TrySetException(e.Exception);
        else
            _recordingStopped.TrySetResult();
    }

    private async Task ProcessChunksAsync()
    {
        await foreach (var chunk in _chunks.Reader.ReadAllAsync())
        {
            using var wavStream = CreateWaveStream(chunk);
            var chunkText = new StringBuilder();

            await foreach (var segment in _processor.ProcessAsync(wavStream))
                chunkText.Append(segment.Text);

            var text = chunkText.ToString().Trim();
            if (text.Length == 0)
                continue;

            _transcript = string.IsNullOrEmpty(_transcript)
                ? text
                : $"{_transcript} {text}";
            TranscriptUpdated?.Invoke(_transcript);
        }
    }

    private static MemoryStream CreateWaveStream(byte[] pcm)
    {
        var stream = new MemoryStream(pcm.Length + 44);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(pcm.Length + 36);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(pcm.Length);
            writer.Write(pcm);
            writer.Flush();
        }

        stream.Position = 0;
        return stream;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_stopTask is null)
                await StopAsync();
        }
        finally
        {
            _capture?.Dispose();
            _processor?.Dispose();
            _factory?.Dispose();
        }
    }
}
