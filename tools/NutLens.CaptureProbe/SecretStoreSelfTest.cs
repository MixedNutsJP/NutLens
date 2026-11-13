using System;
using NutLens.Capture.Security;
using NutLens.Core.Security;

namespace NutLens.CaptureProbe;

/// <summary>
/// 資格情報マネージャーの往復を確かめる（ADR-0020 の宿題）。
///
/// <b>本物の鍵には触らない。</b>別の名前（<see cref="SelfTestName"/>）で書いて読んで消す。
/// 単体テストでは守れない。<c>CredWriteW</c> は Windows 専用 API で、
/// テストは <c>net10.0</c> なので <c>NutLens.Capture</c> を参照できない。
///
/// 一番確かめたいのは <c>CredentialBlobSize</c>。
/// <b>これはバイト長で、文字数を入れると鍵の末尾が切れる。</b>
/// 書き込みも読み出しも成功したまま、Google に拒否されて初めて分かる。
/// </summary>
internal static class SecretStoreSelfTest
{
    private const string SelfTestName = "NutLens/SelfTest";

    public static int Run()
    {
        ISecretStore store = new CredentialManagerSecretStore();

        // 非 ASCII と記号を混ぜる。UTF-16 のバイト長と文字数がずれる文字を必ず含める。
        string sample = "AIzaSy-テスト_鍵-0123456789/+=";

        Console.WriteLine($"名前     : {SelfTestName}");
        Console.WriteLine($"書く値   : {sample.Length} 文字");
        Console.WriteLine();

        try
        {
            store.Write(SelfTestName, sample);
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine($"書き込みに失敗: {exception.Message}");
            return 6;
        }

        string? read = store.TryRead(SelfTestName);
        Console.WriteLine($"読めた値 : {(read is null ? "（null）" : $"{read.Length} 文字")}");

        bool same = string.Equals(read, sample, StringComparison.Ordinal);
        Console.WriteLine(same
            ? "往復     : 一致（末尾が切れていない）"
            : "往復     : **不一致**。CredentialBlobSize がバイト長になっていない可能性が高い");

        store.Delete(SelfTestName);
        string? afterDelete = store.TryRead(SelfTestName);
        bool deleted = afterDelete is null;

        Console.WriteLine(deleted
            ? "削除     : 消えた"
            : "削除     : **消えていない**");

        return same && deleted ? 0 : 9;
    }
}
