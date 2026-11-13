using System;
using System.Collections.Generic;
using NutLens.Core.Imaging;

namespace NutLens.Core.Sampling;

/// <summary>上限枚数まで間引く役（F-06）。</summary>
public interface IFrameThinner
{
    /// <summary>
    /// <paramref name="frames"/> を <paramref name="maxCount"/> 件以下に間引く。
    /// <paramref name="frames"/> は Elapsed の昇順に整列済みであること。
    /// </summary>
    IReadOnlyList<T> Thin<T>(IReadOnlyList<T> frames, int maxCount)
        where T : ITimestamped;
}

/// <summary>
/// 経過時間の軸で均等になるよう間引く（詳細設計 §5.1）。
/// 最初と最後は必ず残る。
/// </summary>
public sealed class EvenTimeFrameThinner : IFrameThinner
{
    public IReadOnlyList<T> Thin<T>(IReadOnlyList<T> frames, int maxCount)
        where T : ITimestamped
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);

        if (frames.Count <= maxCount)
        {
            return frames;
        }

        if (maxCount == 1)
        {
            return new List<T>(1) { frames[0] };
        }

        long first = frames[0].Elapsed.Ticks;
        long last = frames[frames.Count - 1].Elapsed.Ticks;
        long span = last - first;

        SortedSet<int> selected = [];

        // (1) 等間隔の目標時刻ごとに最近傍を選ぶ。
        //     k = 0 と k = maxCount - 1 の目標がちょうど両端なので、最初と最後は必ず入る。
        for (int k = 0; k < maxCount; k++)
        {
            long target = span == 0L
                ? first
                : first + (long)((double)span * k / (maxCount - 1));
            selected.Add(NearestIndex(frames, target));
        }

        // (2) 目標が重複して足りなくなった分を補充する。
        //     選択済みから最も離れているものを足すことで、偏りを広げない。
        while (selected.Count < maxCount)
        {
            int best = -1;
            long bestDistance = -1L;

            for (int i = 0; i < frames.Count; i++)
            {
                if (selected.Contains(i))
                {
                    continue;
                }

                long distance = DistanceToNearestSelected(frames, selected, i);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            if (best < 0)
            {
                break;
            }

            selected.Add(best);
        }

        List<T> result = new(selected.Count);
        foreach (int index in selected)
        {
            result.Add(frames[index]);
        }

        return result;
    }

    /// <summary>目標時刻に最も近いフレームの位置。二分探索。</summary>
    private static int NearestIndex<T>(IReadOnlyList<T> frames, long targetTicks)
        where T : ITimestamped
    {
        int low = 0;
        int high = frames.Count - 1;

        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            if (frames[mid].Elapsed.Ticks < targetTicks)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        if (low > 0)
        {
            long after = Math.Abs(frames[low].Elapsed.Ticks - targetTicks);
            long before = Math.Abs(frames[low - 1].Elapsed.Ticks - targetTicks);
            if (before <= after)
            {
                return low - 1;
            }
        }

        return low;
    }

    private static long DistanceToNearestSelected<T>(
        IReadOnlyList<T> frames,
        SortedSet<int> selected,
        int index)
        where T : ITimestamped
    {
        long ticks = frames[index].Elapsed.Ticks;
        long nearest = long.MaxValue;

        foreach (int other in selected)
        {
            long distance = Math.Abs(frames[other].Elapsed.Ticks - ticks);
            if (distance < nearest)
            {
                nearest = distance;
            }
        }

        return nearest;
    }
}
