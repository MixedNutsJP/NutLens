using System;

namespace NutLens.Core.Distribution;

/// <summary>
/// 版が「安定版か」を判定する（ADR-0039 決定 1 / 決定 3）。
///
/// <para>
/// <b>同じ規則が 2 か所で要る。</b>
/// </para>
/// <list type="bullet">
///   <item>
///     出す側：<c>.github/workflows/release.yml</c> が GitHub Releases の
///     prerelease の印を決める
///   </item>
///   <item>
///     探す側：<see cref="N:NutLens.App.Services"/> の更新確認が、
///     <c>GithubSource</c> に prerelease を探すかどうかを渡す
///   </item>
/// </list>
/// <para>
/// <b>片方だけ変えると壊れる。</b>prerelease として出しているのに安定版だけを探すと、
/// 更新が一度も見つからない。しかも失敗しないので「最新です」と静かに返り続ける。
/// </para>
/// <para>
/// ここを <c>NutLens.Core</c> に置いているのは、<b>単体テストで固めるため</b>。
/// <c>NutLens.App</c>（<c>net10.0-windows</c>）に置くとテスト（<c>net10.0</c>）から
/// 参照できず、規則がずれても気づけない。
/// </para>
/// </summary>
public static class VersionStability
{
    /// <summary>
    /// 安定版ではない（＝ prerelease として扱う）か。
    ///
    /// <para>
    /// <b>接尾辞の判定を先に行う。</b>順序を入れ替えると <c>1.0.0-rc.1</c> が
    /// 「<c>0.</c> で始まらない」ので安定版と判定される。
    /// </para>
    /// </summary>
    /// <param name="version">
    /// 版の文字列。<c>+&lt;コミット&gt;</c> などのビルドメタデータは呼び出し側で落とすこと。
    /// 空や空白のときは <see langword="true"/>（**取りこぼすより余計に探すほうがまし**）。
    /// </param>
    public static bool IsUnstable(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            // 版が読めないなら prerelease も含めて探す。
            // 更新に気づけないほうが、余計に見つけるより困る。
            return true;
        }

        string trimmed = version.Trim();

        return trimmed.Contains('-', StringComparison.Ordinal)
            || trimmed.StartsWith("0.", StringComparison.Ordinal)
            || string.Equals(trimmed, "0", StringComparison.Ordinal);
    }
}
