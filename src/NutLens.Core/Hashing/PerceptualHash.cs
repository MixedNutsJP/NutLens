using System;
using System.Numerics;
using System.Text;

namespace NutLens.Core.Hashing;

/// <summary>
/// 知覚ハッシュの値（ADR-0015 で 64 bit から 576 bit へ広げた）。
///
/// <see cref="ulong"/> の生値を持ち回すと、ビット数が変わったときに
/// 呼び出し側が静かに壊れる。型にしてコンパイラに守らせる。
/// </summary>
public readonly struct PerceptualHash : IEquatable<PerceptualHash>
{
    private readonly ulong[]? words;

    internal PerceptualHash(ulong[] words) => this.words = words;

    /// <summary>ビット列。下位の語から順に並ぶ。</summary>
    public ReadOnlySpan<ulong> Words => this.words ?? [];

    /// <summary>値が入っていない（既定の構造体のまま）か。</summary>
    public bool IsEmpty => this.words is null;

    /// <summary>ハミング距離。0 なら完全一致。</summary>
    public int DistanceTo(PerceptualHash other)
    {
        ReadOnlySpan<ulong> left = this.Words;
        ReadOnlySpan<ulong> right = other.Words;

        if (left.Length != right.Length)
        {
            throw new ArgumentException(
                $"ビット数が違うハッシュは比較できません（{left.Length * 64} と {right.Length * 64}）。",
                nameof(other));
        }

        int total = 0;
        for (int i = 0; i < left.Length; i++)
        {
            total += BitOperations.PopCount(left[i] ^ right[i]);
        }

        return total;
    }

    public bool Equals(PerceptualHash other) =>
        this.Words.SequenceEqual(other.Words);

    public override bool Equals(object? obj) => obj is PerceptualHash other && this.Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (ulong word in this.Words)
        {
            hash.Add(word);
        }

        return hash.ToHashCode();
    }

    /// <summary>16 進。ログと確認ツールで読むためのもの。</summary>
    public override string ToString()
    {
        ReadOnlySpan<ulong> value = this.Words;
        if (value.IsEmpty)
        {
            return "(なし)";
        }

        StringBuilder builder = new(value.Length * 16);
        for (int i = value.Length - 1; i >= 0; i--)
        {
            builder.Append(value[i].ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public static bool operator ==(PerceptualHash left, PerceptualHash right) => left.Equals(right);

    public static bool operator !=(PerceptualHash left, PerceptualHash right) => !left.Equals(right);
}
