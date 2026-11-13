using System;
using System.Collections.Generic;
using System.Text;

namespace NutLens.Core.Media;

/// <summary>
/// OCR で読んだ行を集め、重複を除く（F-12 / ADR-0018 決定 3）。
///
/// ショート動画はループ再生され、テロップは数秒出しっぱなしになる。
/// 同じ文字列が何度も出るので、最初に出たフレームの時刻だけを残す。
///
/// 画素に触らない純ロジックなので Core に置く（ADR-0002 決定 2）。
/// </summary>
public sealed class OcrTextCollector
{
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly List<OcrLine> lines = [];

    public int Count => this.lines.Count;

    /// <summary>受理したら true。既に同じ内容があれば false。</summary>
    public bool TryAdd(TimeSpan elapsed, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // 認識器が入れた不自然な空白だけを詰める（ADR-0018 実装で分かったこと 2）。
        string cleaned = CollapseCjkSpacing(text.Trim());
        if (cleaned.Length == 0)
        {
            return false;
        }

        if (!this.seen.Add(Normalize(cleaned)))
        {
            return false;
        }

        this.lines.Add(new OcrLine(elapsed, cleaned));
        return true;
    }

    public IReadOnlyList<OcrLine> ToLines() => this.lines;

    /// <summary>
    /// 日本語の文字どうしの間に入った空白を詰める。
    ///
    /// Windows の日本語 OCR は 1 文字ずつを「語」として返すため、
    /// 行の文字列が「タ ク シ ー に 乗 っ た ら」のように分かち書きされる。
    /// これは認識器の都合であって、画面に出ていた文字列ではない。
    ///
    /// <b>英数字の間の空白は残す。</b>そこは本当に語の区切りで、
    /// 詰めると「TAXi 3」が「TAXi3」になるなど、画面に無い文字列を作ってしまう。
    /// </summary>
    internal static string CollapseCjkSpacing(string text)
    {
        StringBuilder builder = new(text.Length);
        int index = 0;

        while (index < text.Length)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                builder.Append(text[index]);
                index++;
                continue;
            }

            // 空白の連なりをまとめて見る。「タクシー  に」のように 2 つ以上あることがある。
            int end = index;
            while (end < text.Length && char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            bool atEdge = builder.Length == 0 || end >= text.Length;
            if (!atEdge && !(IsCjk(builder[^1]) && IsCjk(text[end])))
            {
                builder.Append(' ');
            }

            index = end;
        }

        return builder.ToString();
    }

    /// <summary>
    /// かな・漢字・日本語の記号か。
    ///
    /// <b>全角の英数字（Ａ〜Ｚ・０〜９）は含めない。</b>
    /// そこは語の区切りとしての空白がありうるので、半角の英数字と同じ扱いにする。
    /// </summary>
    private static bool IsCjk(char value) => value switch
    {
        >= '　' and <= 'ヿ' => true,   // 句読点・ひらがな・カタカナ
        >= '㐀' and <= '䶿' => true,   // CJK 拡張 A
        >= '一' and <= '鿿' => true,   // CJK 統合漢字
        >= '豈' and <= '﫿' => true,   // CJK 互換漢字
        >= 'ｦ' and <= 'ﾟ' => true,   // 半角カタカナ
        _ => false,
    };

    /// <summary>
    /// 重複判定のための正規化。
    ///
    /// <b>大文字小文字は揃えない。</b>「TAXi3」と「taxi3」を同じにしてしまうと、
    /// 固有名詞の表記という手がかりが失われる。
    /// </summary>
    internal static string Normalize(string text)
    {
        // 全角と半角を揃える。「１」と「1」が別物にならないように。
        string normalized = text.Normalize(NormalizationForm.FormKC);

        StringBuilder builder = new(normalized.Length);
        bool lastWasSpace = false;

        foreach (char character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                lastWasSpace = true;
                continue;
            }

            if (lastWasSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(character);
            lastWasSpace = false;
        }

        return builder.ToString();
    }
}
