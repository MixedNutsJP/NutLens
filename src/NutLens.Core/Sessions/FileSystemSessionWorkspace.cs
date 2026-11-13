using System;
using System.Collections.Generic;
using System.IO;
using NutLens.Core.Imaging;

namespace NutLens.Core.Sessions;

/// <summary>
/// 一時作業フォルダの作成・書き出し・削除（F-07 / 5.5）。
///
/// 削除の責務をこのインタフェースに閉じる。消す処理を他所に書かないことで、
/// 「消し漏れていないか」をこの実装 1 つを読めば確かめられる状態を保つ。
/// </summary>
public interface ISessionWorkspace
{
    /// <summary>セッションフォルダと frames/ を作る。</summary>
    SessionLayout Create(SessionId id);

    /// <summary>frames/ へ書き出し、書いたファイルのフルパスを撮影順に返す。</summary>
    IReadOnlyList<string> WriteFrames(SessionLayout layout, IReadOnlyList<EncodedFrame> frames);

    bool Exists(SessionId id);

    /// <summary>1 セッション分を完全に消す。存在しなくても例外にしない。</summary>
    void Delete(SessionId id);

    /// <summary>
    /// work\ の全子フォルダを消し、消した数を返す。
    /// 起動時（異常終了の残骸回収）、次の撮影の開始時、アプリ終了時に呼ぶ。
    /// </summary>
    int DeleteAll();
}

/// <inheritdoc />
public sealed class FileSystemSessionWorkspace : ISessionWorkspace
{
    private readonly IAppDataPaths paths;

    public FileSystemSessionWorkspace(IAppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.paths = paths;
    }

    public SessionLayout Create(SessionId id)
    {
        SessionLayout layout = new(this.paths.WorkRoot, id);
        Directory.CreateDirectory(layout.FramesDirectory);
        return layout;
    }

    public IReadOnlyList<string> WriteFrames(SessionLayout layout, IReadOnlyList<EncodedFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(frames);

        Directory.CreateDirectory(layout.FramesDirectory);

        List<string> written = new(frames.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            EncodedFrame frame = frames[i];
            string name = SessionLayout.FrameFileName(i + 1, frame.Elapsed, frame.FileExtension);
            string path = Path.Combine(layout.FramesDirectory, name);
            File.WriteAllBytes(path, frame.Bytes);
            written.Add(path);
        }

        return written;
    }

    public bool Exists(SessionId id)
    {
        SessionLayout layout = new(this.paths.WorkRoot, id);
        return Directory.Exists(layout.Root);
    }

    public void Delete(SessionId id)
    {
        SessionLayout layout = new(this.paths.WorkRoot, id);
        DeleteDirectory(layout.Root);
    }

    public int DeleteAll()
    {
        if (!Directory.Exists(this.paths.WorkRoot))
        {
            return 0;
        }

        int deleted = 0;
        foreach (string directory in Directory.EnumerateDirectories(this.paths.WorkRoot))
        {
            if (DeleteDirectory(directory))
            {
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>
    /// 消せなかった場合に例外を外へ出さない。
    /// 起動時の残骸掃除で、他のプロセスが掴んでいるフォルダ 1 つのためにアプリが起動しないのは割に合わない。
    /// </summary>
    private static bool DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        try
        {
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
