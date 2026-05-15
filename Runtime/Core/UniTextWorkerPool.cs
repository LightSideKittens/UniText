using System;
using System.Threading;
using UnityEngine;
using ThreadPriority = System.Threading.ThreadPriority;

namespace LightSide
{
    /// <summary>
    /// Thread pool for parallel text processing across multiple UniText components.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Manages a pool of worker threads (CPU count - 1) that can process UniText
    /// components in parallel during the first pass (shaping, BiDi analysis).
    /// </para>
    /// <para>
    /// Uses a barrier-based synchronization model: the main thread distributes work
    /// across workers and waits for all to complete before proceeding.
    /// </para>
    /// </remarks>
    /// <seealso cref="UniText"/>
    internal static class UniTextWorkerPool
    {
        private static readonly int ThreadCount = Math.Max(1, Environment.ProcessorCount - 1);
        private const int BarrierTimeoutMs = 5000;

        private static Thread[] workers;
        private static AutoResetEvent[] workReady;
        private static CountdownEvent barrier;
        private static volatile bool isInitialized;
        private static volatile bool isShuttingDown;

    #if UNITY_EDITOR
        static UniTextWorkerPool()
        {
            Reseter.ManagedCleaning += ForceShutdown;
        }
    #endif

        private static void ForceShutdown()
        {
            lock (typeof(UniTextWorkerPool))
            {
                isShuttingDown = true;

                if (workReady != null)
                {
                    for (var i = 0; i < workReady.Length; i++)
                        workReady[i]?.Set();
                }

                if (workers != null)
                {
                    for (var i = 0; i < workers.Length; i++)
                        workers[i]?.Join(50);
                }

                if (workReady != null)
                {
                    for (var i = 0; i < workReady.Length; i++)
                    {
                        try { workReady[i]?.Dispose(); } catch { }
                    }
                }

                try { barrier?.Dispose(); } catch { }

                workers = null;
                workReady = null;
                barrier = null;
                threadStartIndices = null;
                threadEndIndices = null;
                threadExceptions = null;
                currentComponents = null;
                currentAction = null;
                isInitialized = false;
                isShuttingDown = false;
            }
        }

        private static UniText[] currentComponents;
        private static int currentComponentCount;
        private static Action<UniText> currentAction;

        private static int[] threadStartIndices;
        private static int[] threadEndIndices;

        private static Exception[] threadExceptions;
        private static volatile int exceptionCount;

        /// <summary>Returns true if parallel processing is supported (more than 1 CPU core, not WebGL).</summary>
        public static bool IsParallelSupported =>
#if UNITY_WEBGL && !UNITY_EDITOR
            false;
#else
            ThreadCount > 1;
#endif

        /// <summary>Ensures the worker pool is initialized.</summary>
        public static void EnsureInitialized()
        {
            if (isInitialized || isShuttingDown) return;

            lock (typeof(UniTextWorkerPool))
            {
                if (isInitialized || isShuttingDown) return;

                isShuttingDown = false;

                workers = new Thread[ThreadCount];
                workReady = new AutoResetEvent[ThreadCount];
                threadStartIndices = new int[ThreadCount];
                threadEndIndices = new int[ThreadCount];
                threadExceptions = new Exception[ThreadCount];
                barrier = new CountdownEvent(1);

                for (var i = 0; i < ThreadCount; i++)
                {
                    workReady[i] = new AutoResetEvent(false);

                    var threadIdx = i;
                    workers[i] = new Thread(() => WorkerLoop(threadIdx))
                    {
                        IsBackground = true,
                        Name = $"UniTextWorker_{i}",
                        Priority = ThreadPriority.Normal
                    };
                    workers[i].Start();
                }

                Application.quitting += Shutdown;
                isInitialized = true;

                Cat.MeowFormat("[UniTextWorkerPool] Initialized with {0} threads", ThreadCount);
            }
        }

        private static void Shutdown()
        {
            if (!isInitialized) return;

            isShuttingDown = true;

            for (var i = 0; i < ThreadCount; i++)
                workReady[i].Set();

            for (var i = 0; i < ThreadCount; i++)
                workers[i].Join(100);

            isInitialized = false;
        }

        private static void WorkerLoop(int threadIdx)
        {
            while (!isShuttingDown)
            {
                try
                {
                    workReady[threadIdx].WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (isShuttingDown) break;

                var localBarrier = barrier;
                if (localBarrier == null) break;

                try
                {
                    var starts = threadStartIndices;
                    var ends = threadEndIndices;
                    var components = currentComponents;
                    var action = currentAction;

                    if (starts != null && ends != null && components != null && action != null)
                    {
                        var start = starts[threadIdx];
                        var end = ends[threadIdx];

                        for (var i = start; i < end; i++)
                        {
                            var comp = components[i];
                            if (comp != null)
                                action(comp);
                        }
                    }
                }
                catch (Exception ex)
                {
                    var exceptions = threadExceptions;
                    if (exceptions != null)
                    {
                        exceptions[threadIdx] = ex;
                        Interlocked.Increment(ref exceptionCount);
                    }
                }

                try
                {
                    localBarrier.Signal();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        /// <summary>Executes an action on multiple UniText components, optionally in parallel.</summary>
        /// <param name="components">Array of components to process.</param>
        /// <param name="count">Number of components in the array.</param>
        /// <param name="action">Action to execute on each component.</param>
        public static void Execute(UniText[] components, int count, Action<UniText> action)
        {
            if (count == 0) return;

            if (count == 1 || !IsParallelSupported || isShuttingDown)
            {
                for (var i = 0; i < count; i++)
                {
                    var comp = components[i];
                    if (comp != null)
                        action(comp);
                }
                return;
            }

            EnsureInitialized();

            if (!isInitialized || isShuttingDown)
            {
                for (var i = 0; i < count; i++)
                {
                    var comp = components[i];
                    if (comp != null)
                        action(comp);
                }
                return;
            }

            currentComponents = components;
            currentComponentCount = count;
            currentAction = action;
            exceptionCount = 0;

            var perThread = count / ThreadCount;
            var remainder = count % ThreadCount;
            var offset = 0;

            for (var i = 0; i < ThreadCount; i++)
            {
                threadStartIndices[i] = offset;
                var threadCount = perThread + (i < remainder ? 1 : 0);
                offset += threadCount;
                threadEndIndices[i] = offset;
                threadExceptions[i] = null;
            }

            barrier.Reset(ThreadCount);

            for (var i = 0; i < ThreadCount; i++)
                workReady[i].Set();

            try
            {
                var localBarrier = barrier;
                if (localBarrier != null && !isShuttingDown)
                {
                    const int pollIntervalMs = 10;
                    var elapsed = 0;
                    while (!localBarrier.Wait(pollIntervalMs) && !isShuttingDown && elapsed < BarrierTimeoutMs)
                    {
                        elapsed += pollIntervalMs;
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }

            currentComponents = null;
            currentAction = null;

            if (exceptionCount > 0)
            {
                for (var i = 0; i < ThreadCount; i++)
                {
                    if (threadExceptions[i] != null)
                    {
                        Debug.LogException(threadExceptions[i]);
                        threadExceptions[i] = null;
                    }
                }
            }
        }
    }

}
