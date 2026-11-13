using System;
using System.Collections.Generic;
using NutLens.Core.Imaging;

namespace NutLens.Core.Hashing;

/// <summary>ほぼ同じフレームを棄却する役（F-05）。</summary>
public interface IDuplicateFrameFilter
{
    /// <summary>これ以下のハミング距離を「同じ場面」とみなす。</summary>
    int Threshold { get; }

    /// <summary>これまでに受理した枚数。</summary>
    int AcceptedCount { get; }

    /// <summary>受理したら true。棄却したら false。</summary>
    bool TryAccept(CapturedFrame frame);

    /// <summary>受理済みの記録を捨てる。撮影を始め直すときに呼ぶ。</summary>
    void Reset();
}

/// <summary>しきい値を指定して <see cref="IDuplicateFrameFilter"/> を作る役。</summary>
public interface IDuplicateFrameFilterFactory
{
    IDuplicateFrameFilter Create(int threshold);
}

/// <summary>
/// dHash による重複判定（ADR-0004）。
///
/// 直前のフレームではなく <b>受理済みフレーム全件</b>と比較する。
/// ショート動画はループ再生されるため、同じ場面が周期的に戻ってくる。
/// 直前との比較だけでは、間に別の場面が挟まった再訪が一切落ちない。
/// </summary>
public sealed class DHashDuplicateFrameFilter : IDuplicateFrameFilter
{
    public const int MinThreshold = 0;

    /// <summary>
    /// 上限。576 bit のうち 64 bit まで。
    /// これを超えると、ほとんどの場面が「同じ」と判定されてしまう。
    /// </summary>
    public const int MaxThreshold = 64;

    /// <summary>
    /// 既定値（ADR-0015 決定 2）。
    /// 縮小によるずれの実測が 5、別の場面どうしの最小距離が 23 で、そのほぼ中間。
    /// <b>ループ再生の繰り返しが何ビット離れるかは測っていない</b>（ADR-0015 宿題）。
    /// </summary>
    public const int DefaultThreshold = 12;

    private readonly List<PerceptualHash> acceptedHashes = [];

    public DHashDuplicateFrameFilter(int threshold)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, MinThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(threshold, MaxThreshold);
        this.Threshold = threshold;
    }

    public int Threshold { get; }

    public int AcceptedCount => this.acceptedHashes.Count;

    public bool TryAccept(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        PerceptualHash hash = DHash.Compute(frame);

        foreach (PerceptualHash accepted in this.acceptedHashes)
        {
            if (hash.DistanceTo(accepted) <= this.Threshold)
            {
                return false;
            }
        }

        this.acceptedHashes.Add(hash);
        return true;
    }

    public void Reset() => this.acceptedHashes.Clear();
}

/// <summary><see cref="DHashDuplicateFrameFilter"/> を作る既定の実装。</summary>
public sealed class DHashDuplicateFrameFilterFactory : IDuplicateFrameFilterFactory
{
    public IDuplicateFrameFilter Create(int threshold) => new DHashDuplicateFrameFilter(threshold);
}
