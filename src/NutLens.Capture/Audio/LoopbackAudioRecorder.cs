using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NutLens.Core.Media;

namespace NutLens.Capture.Audio;

/// <summary>
/// WASAPI ループバックでシステム音声を録る（F-11 / ADR-0016）。
///
/// 録りながら 16 kHz・モノラル・16 bit に落とす。
/// デバイスの形式（48 kHz・2 ch・32 bit float など）のまま 60 秒録ると 20 MB を超え、
/// 一時データとして扱う量が無駄に増える（5.5 は量を絞ることを求めている）。
///
/// <b>ループバックはスピーカーに出ている音すべてを拾う。</b>
/// 通知音や他のアプリの音も入ることは、画面で利用者に伝える（ADR-0016 決定 4）。
/// </summary>
public sealed class LoopbackAudioRecorder : IAudioRecorder
{
    /// <summary>whisper が内部で使う形式に合わせる。</summary>
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;

    /// <summary>
    /// 書き出しの上限（バイト）。
    ///
    /// 撮影時間の上限は 3600 秒で、16 kHz・モノラル・16 bit なら約 115 MB。
    /// それを超えたら何かが壊れているので、書くのを止める。
    /// <b>この歯止めが無いと、不具合が「ディスクが埋まる」形で表れる。</b>
    /// </summary>
    public const long MaxBytes = 128L * 1024 * 1024;

    private readonly SemaphoreSlim stopped = new(0, 1);

    private WasapiRecorder? capture;
    private MediaFoundationResampler? resampler;
    private BufferedWaveProvider? buffer;
    private WaveFileWriter? writer;
    private string? destination;
    private int disposed;
    private bool overflowed;

    public string? FailureReason { get; private set; }

    public bool TryStart(string destinationWavPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationWavPath);

        try
        {
            // 既定の再生デバイスのループバック。デバイスを指定しなければ既定が使われる。
            // プロセス単位のループバック（WithProcessLoopback）も使えるが、
            // 要件は「システム音声」なので使わない（ADR-0016 却下案）。
            this.capture = new WasapiRecorderBuilder()
                .WithLoopbackCapture()
                .Build();

            // 取り込んだ波形を一度ためてから、必要なぶんだけ変換して書く。
            // 変換器が「読みたいだけ読む」形なので、間に緩衝が要る。
            this.buffer = new BufferedWaveProvider(this.capture.WaveFormat, TimeSpan.FromSeconds(5))
            {
                DiscardOnBufferOverflow = true,

                // **既定は true。** true のままだと、たまった音が尽きても無音を詰めて返し続け、
                // 「取れるだけ取って書く」ループが永久に回る。
                // 実際にこれで 1.2 GB の WAV が生成された。
                ReadFully = false,
            };

            WaveFormat target = new(SampleRate, BitsPerSample, Channels);
            this.resampler = new MediaFoundationResampler(this.buffer, target)
            {
                ResamplerQuality = 60,
            };

            string? directory = Path.GetDirectoryName(destinationWavPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            this.writer = new WaveFileWriter(destinationWavPath, target);
            this.destination = destinationWavPath;

            // NAudio 3.x の DataAvailable は ReadOnlySpan を渡す零コピーの委譲なので、
            // メソッド参照ではなくラムダで受ける（型は委譲から推論される）。
            this.capture.DataAvailable += (buffer, flags, devicePosition, qpcPosition) =>
                this.OnDataAvailable(buffer);
            this.capture.RecordingStopped += this.OnRecordingStopped;
            this.capture.StartRecording();

            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Runtime.InteropServices.COMException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            // 再生デバイスが無い／使えない。撮影自体は続ける（ADR-0016 決定 3）。
            this.FailureReason = exception.Message;
            this.CleanUp();
            return false;
        }
    }

    public async Task<long> StopAsync()
    {
        if (this.capture is null)
        {
            return 0;
        }

        this.capture.StopRecording();

        // RecordingStopped は別スレッドで来る。待たずに writer を閉じると、
        // 書き込み中のバッファと衝突する。
        await this.stopped.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        long length = this.writer?.Length ?? 0;

        if (this.overflowed)
        {
            this.FailureReason =
                $"録音が上限（{MaxBytes / 1024 / 1024} MB）に達したため打ち切りました。";
        }

        this.CleanUp();

        // 中身が無いファイルを残さない。後段が「音声がある」と誤解する。
        if (length == 0 && this.destination is not null && File.Exists(this.destination))
        {
            TryDelete(this.destination);
        }

        return length;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) == 1)
        {
            return;
        }

        if (this.capture is not null)
        {
            await this.StopAsync().ConfigureAwait(false);
        }

        this.stopped.Dispose();
    }

    private void OnDataAvailable(ReadOnlySpan<byte> captured)
    {
        if (this.buffer is null || this.resampler is null || this.writer is null || captured.IsEmpty)
        {
            return;
        }

        try
        {
            this.buffer.AddSamples(captured);

            // 変換器から取れるだけ取って書く。
            // ReadFully = false にしてあるので、入力が尽きれば Read は 0 を返す。
            Span<byte> converted = stackalloc byte[4096];
            int read;
            while ((read = this.resampler.Read(converted)) > 0)
            {
                if (this.writer.Length >= MaxBytes)
                {
                    // 歯止め。ここに来る時点で何かが壊れている。
                    this.overflowed = true;
                    return;
                }

                this.writer.Write(converted[..read]);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or ObjectDisposedException)
        {
            // 停止と行き違った。ここで落とすと撮影ごと巻き込む。
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        try
        {
            this.writer?.Flush();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }

        if (args.Exception is not null)
        {
            this.FailureReason = args.Exception.Message;
        }

        if (this.stopped.CurrentCount == 0)
        {
            this.stopped.Release();
        }
    }

    private void CleanUp()
    {
        if (this.capture is not null)
        {
            this.capture.RecordingStopped -= this.OnRecordingStopped;
            this.capture.Dispose();
            this.capture = null;
        }

        this.resampler?.Dispose();
        this.resampler = null;

        this.writer?.Dispose();
        this.writer = null;

        this.buffer = null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>撮影ごとに録音器を作る。録音器は状態を持つので使い回さない。</summary>
public sealed class LoopbackAudioRecorderFactory : IAudioRecorderFactory
{
    public IAudioRecorder Create() => new LoopbackAudioRecorder();
}
