namespace NutLens.Core.Security;

/// <summary>
/// 秘密の文字列を、設定ファイルの外に預ける役（5.6 / ADR-0020）。
///
/// 実装は Windows 資格情報マネージャー（<c>NutLens.Capture</c> 側）。
/// <b>読んだ値を保持しない。</b>呼ぶ側が使う直前に読み、使い終わったら捨てる。
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// 無ければ null。
    /// <b>例外にしない。</b>「まだ入れていない」は異常ではなく、既定の状態（5.6 は既定オフ）。
    /// </summary>
    string? TryRead(string name);

    void Write(string name, string secret);

    /// <summary>無くても成功扱い。消す口は必ず用意する（ADR-0020 決定 5）。</summary>
    void Delete(string name);
}

/// <summary>預ける秘密の名前。資格情報マネージャーの一覧にこの名前で並ぶ。</summary>
public static class SecretNames
{
    /// <summary>Google Cloud Vision の API キー（F-14 / 5.6）。</summary>
    public const string GoogleCloudVisionApiKey = "NutLens/GoogleCloudVision";
}
