using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using UnityEngine.Profiling;

namespace LightSide
{
    /// <summary>
    /// Lightweight runtime profiler and debug utilities for UniText.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BeginSample"/>/<see cref="EndSample"/> work in ALL builds (no compile-time stripping).
    /// When <see cref="Enabled"/> is false, overhead is a single branch per call (inlined).
    /// </para>
    /// <para>
    /// When enabled, accumulates per-sample timing and managed allocation data per frame.
    /// Call <see cref="FlushFrameLog"/> at end of frame to log a summary and reset.
    /// </para>
    /// </remarks>
    internal static class UniTextDebug
    {
        /// <summary>Master enable flag. When false, BeginSample/EndSample are near-zero-cost no-ops.</summary>
        public static bool Enabled;

        /// <summary>Also forward to Unity Profiler (development builds only).</summary>
        public static bool ProfilerEnabled = true;

        /// <summary>Enable performance counters.</summary>
        public static bool CountersEnabled = true;


        #region Lightweight Profiler

        private const int MaxEntries = 64;
        private const int MaxStack = 16;

        private static int entryCount;
        private static readonly string[] entryNames = new string[MaxEntries];
        private static readonly long[] entryTicks = new long[MaxEntries];
        private static readonly int[] entryCounts = new int[MaxEntries];
        private static readonly long[] entryAlloc = new long[MaxEntries];

        private static int stackDepth;
        private static readonly long[] stackStartTicks = new long[MaxStack];
        private static readonly long[] stackStartAlloc = new long[MaxStack];
        private static readonly int[] stackEntryIdx = new int[MaxStack];

        private static bool hasFrameData;
        private static bool allocTrackingAvailable = true;

        private static readonly double ticksToMs = 1000.0 / Stopwatch.Frequency;

        /// <summary>Begins a named timing sample. Supports nesting up to 16 levels.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("UNITEXT_PROFILE")]
        public static void BeginSample(string name)
        {
            if (!Enabled) return;
            BeginSampleCore(name);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("UNITEXT_PROFILE")]
        private static void BeginSampleCore(string name)
        {
            if (ProfilerEnabled)
                Profiler.BeginSample(name);

            if (stackDepth >= MaxStack) return;

            int idx = FindOrCreateEntry(name);
            stackStartTicks[stackDepth] = Stopwatch.GetTimestamp();
            stackStartAlloc[stackDepth] = GetThreadAllocBytes();
            stackEntryIdx[stackDepth] = idx;
            stackDepth++;
        }

        /// <summary>Ends the most recent sample, accumulating elapsed time and allocations.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("UNITEXT_PROFILE")]
        public static void EndSample()
        {
            if (!Enabled) return;
            EndSampleCore();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Conditional("UNITEXT_PROFILE")]
        private static void EndSampleCore()
        {
            if (stackDepth <= 0)
            {
                if (ProfilerEnabled) Profiler.EndSample();
                return;
            }
            stackDepth--;

            long now = Stopwatch.GetTimestamp();
            long allocNow = GetThreadAllocBytes();

            int idx = stackEntryIdx[stackDepth];
            entryTicks[idx] += now - stackStartTicks[stackDepth];
            entryCounts[idx]++;

            if (allocNow >= 0)
                entryAlloc[idx] += allocNow - stackStartAlloc[stackDepth];

            hasFrameData = true;

            if (ProfilerEnabled)
                Profiler.EndSample();
        }

        private static int FindOrCreateEntry(string name)
        {
            for (int i = 0; i < entryCount; i++)
                if (ReferenceEquals(entryNames[i], name))
                    return i;

            if (entryCount >= MaxEntries) return MaxEntries - 1;
            int idx = entryCount++;
            entryNames[idx] = name;
            entryTicks[idx] = 0;
            entryCounts[idx] = 0;
            entryAlloc[idx] = 0;
            return idx;
        }

        private static long GetThreadAllocBytes()
        {
            if (!allocTrackingAvailable) return -1;
            try { return System.GC.GetAllocatedBytesForCurrentThread(); }
            catch { allocTrackingAvailable = false; return -1; }
        }

        /// <summary>
        /// Logs a one-line summary of accumulated frame data, then resets for next frame.
        /// No-op if nothing was sampled.
        /// </summary>
        [Conditional("UNITEXT_PROFILE")]
        public static void FlushFrameLog()
        {
            if (!Enabled || !hasFrameData) return;

            var sb = new StringBuilder(256);
            sb.Append("[UniText Timing]");

            for (int i = 0; i < entryCount; i++)
            {
                if (entryCounts[i] == 0) continue;
                double ms = entryTicks[i] * ticksToMs;
                sb.Append(' ');
                sb.Append(entryNames[i]);
                sb.Append(':');
                sb.AppendFormat("{0:F2}ms", ms);
                if (entryCounts[i] > 1)
                {
                    sb.Append('×');
                    sb.Append(entryCounts[i]);
                }
                long alloc = entryAlloc[i];
                if (alloc > 0)
                {
                    sb.Append('(');
                    FormatBytes(sb, alloc);
                    sb.Append(')');
                }
            }

            if (!allocTrackingAvailable)
                sb.Append(" [alloc tracking unavailable]");

            UnityEngine.Debug.Log(sb.ToString());
            ResetFrame();
        }

        /// <summary>Resets per-frame data without logging.</summary>
        [Conditional("UNITEXT_PROFILE")]
        public static void ResetFrame()
        {
            for (int i = 0; i < entryCount; i++)
            {
                entryTicks[i] = 0;
                entryCounts[i] = 0;
                entryAlloc[i] = 0;
            }
            stackDepth = 0;
            hasFrameData = false;
        }

        /// <summary>Clears all entries. Call when switching test phases to avoid stale names.</summary>
        [Conditional("UNITEXT_PROFILE")]
        public static void ResetEntries()
        {
            ResetFrame();
            entryCount = 0;
        }

        private static void FormatBytes(StringBuilder sb, long bytes)
        {
            if (bytes < 1024) { sb.Append(bytes); sb.Append("B"); }
            else if (bytes < 1024 * 1024) { sb.AppendFormat("{0:F1}KB", bytes / 1024f); }
            else { sb.AppendFormat("{0:F2}MB", bytes / (1024f * 1024f)); }
        }

        #endregion


        #region Counters

        public static int TextProcessor_ProcessCount;
        public static int TextProcessor_EnsureShapingCount;
        public static int TextProcessor_DoFullShapingCount;

        public static int Buffers_InstanceCount;
        public static int Buffers_RentCount;

        public static int Bidi_ProcessCount;
        public static int Bidi_BuildIsoRunSeqCount;
        public static int Bidi_BuildIsoRunSeqForParagraphCount;

        public static int Pool_TotalRents;
        public static int Pool_PoolHits;
        public static int Pool_PoolMisses;
        public static int Pool_SharedHits;
        public static int Pool_TotalReturns;
        public static int Pool_ReturnRejectedTooLarge;
        public static int Pool_ReturnRejectedWrongSize;
        public static int Pool_ReturnRejectedPoolFull;
        public static int Pool_CumulativeRents;
        public static int Pool_CumulativeReturns;
        public static int Pool_CumulativeAllocations;
        public static int Pool_LargestRentRequested;

        #endregion


        #region Counter Wrappers

        /// <summary>Thread-safely increments a counter if debug and counters are enabled.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("UNITEXT_POOL_DEBUG")]
        public static void Increment(ref int counter)
        {
            if (Enabled && CountersEnabled)
                Interlocked.Increment(ref counter);
        }

        /// <summary>Thread-safely updates a counter to track the maximum value seen.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Conditional("UNITEXT_POOL_DEBUG")]
        public static void TrackLargest(ref int current, int value)
        {
            if (!Enabled || !CountersEnabled) return;

            int snapshot;
            do
            {
                snapshot = current;
                if (value <= snapshot) return;
            } while (Interlocked.CompareExchange(ref current, value, snapshot) != snapshot);
        }

        #endregion


        #region Reset & Reporting

        /// <summary>Resets all performance counters to zero.</summary>
        [Conditional("UNITEXT_POOL_DEBUG")]
        public static void ResetAllCounters()
        {
            TextProcessor_ProcessCount = 0;
            TextProcessor_EnsureShapingCount = 0;
            TextProcessor_DoFullShapingCount = 0;

            Buffers_InstanceCount = 0;
            Buffers_RentCount = 0;

            Bidi_ProcessCount = 0;
            Bidi_BuildIsoRunSeqCount = 0;
            Bidi_BuildIsoRunSeqForParagraphCount = 0;

            Pool_TotalRents = 0;
            Pool_PoolHits = 0;
            Pool_PoolMisses = 0;
            Pool_SharedHits = 0;
            Pool_TotalReturns = 0;
            Pool_ReturnRejectedTooLarge = 0;
            Pool_ReturnRejectedWrongSize = 0;
            Pool_ReturnRejectedPoolFull = 0;
            Pool_CumulativeRents = 0;
            Pool_CumulativeReturns = 0;
            Pool_CumulativeAllocations = 0;
            Pool_LargestRentRequested = 0;
        }

        /// <summary>Generates a formatted performance report with all counter values.</summary>
        public static string GetReport()
        {
            return $@"=== UniText Debug Report ===

    TextProcessor:
      Process calls: {TextProcessor_ProcessCount}
      EnsureShaping calls: {TextProcessor_EnsureShapingCount}
      DoFullShaping calls: {TextProcessor_DoFullShapingCount}

    Buffers:
      Instances: {Buffers_InstanceCount}
      Rent calls: {Buffers_RentCount}

    BidiEngine:
      Process calls: {Bidi_ProcessCount}
      BuildIsoRunSeq calls: {Bidi_BuildIsoRunSeqCount}
      BuildIsoRunSeqForParagraph calls: {Bidi_BuildIsoRunSeqForParagraphCount}

    ArrayPool:
      Total rents: {Pool_TotalRents}
      Pool hits: {Pool_PoolHits}
      Pool misses: {Pool_PoolMisses}
      Shared hits: {Pool_SharedHits}
      Total returns: {Pool_TotalReturns}
      Rejected (too large): {Pool_ReturnRejectedTooLarge}
      Rejected (wrong size): {Pool_ReturnRejectedWrongSize}
      Rejected (pool full): {Pool_ReturnRejectedPoolFull}
      Cumulative rents: {Pool_CumulativeRents}
      Cumulative returns: {Pool_CumulativeReturns}
      Cumulative allocations: {Pool_CumulativeAllocations}
      Largest rent requested: {Pool_LargestRentRequested}

    Pool efficiency: {(Pool_TotalRents > 0 ? (Pool_PoolHits + Pool_SharedHits) * 100f / Pool_TotalRents : 0):F1}%
    ";
        }

        /// <summary>Logs the performance report to Unity console via Debug.Log.</summary>
        [Conditional("UNITEXT_POOL_DEBUG")]
        public static void LogReport()
        {
            UnityEngine.Debug.Log(GetReport());
        }

        #endregion
    }

    /// <summary>
    /// Zero-cost phased stopwatch. <see cref="Mark"/> is
    /// <c>[Conditional("UNITEXT_DEBUG")]</c> — stripped in release.
    /// <see cref="Phase"/> and <see cref="Total"/> always compile but return 0 when no marks recorded.
    /// Use with <see cref="Cat.Meow"/> which also strips — no <c>#if</c> needed at call sites.
    /// </summary>
    internal unsafe struct DebugTimer
    {
        private const int MaxMarks = 8;
        private fixed long marks[MaxMarks];
        private int count;

        /// <summary>Records a timestamp. First call = start, subsequent calls = phase boundaries.</summary>
        [Conditional("UNITEXT_DEBUG")]
        public void Mark()
        {
            if (count < MaxMarks)
                marks[count++] = Stopwatch.GetTimestamp();
        }

        /// <summary>Returns the duration of phase <paramref name="index"/> in milliseconds.
        /// Phase 0 = Mark(0)→Mark(1), last phase = last mark→now.</summary>
        public double Phase(int index)
        {
            if ((uint)index >= (uint)count) return 0;
            long from = marks[index];
            long to = index + 1 < count ? marks[index + 1] : Stopwatch.GetTimestamp();
            return (to - from) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Returns total elapsed time from first <see cref="Mark"/> to now.</summary>
        public double Total => count < 1 ? 0 : (Stopwatch.GetTimestamp() - marks[0]) * 1000.0 / Stopwatch.Frequency;
    }
}
