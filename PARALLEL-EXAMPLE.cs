// Example: Using Parallel Force Calculations in Engine13
// This file demonstrates how to configure and benchmark parallel force processing

using System;
using System.Diagnostics;
using Engine13.Core;

namespace Engine13.Examples
{
    public class ParallelForceExample
    {
        // Example 1: Basic Configuration
        public static void ConfigureParallelProcessing(UpdateManager updateManager)
        {
            // Enable parallel force calculations (default: enabled)
            updateManager.EnableParallelForces = true;
            
            // Set threshold for when to use parallel processing
            // Below this count, sequential processing is used
            updateManager.ParallelThreshold = 100; // Default: 100
            
            Console.WriteLine("Parallel force calculation enabled");
            Console.WriteLine($"Will parallelize when entity count >= {updateManager.ParallelThreshold}");
        }

        // Example 2: Adaptive Threshold Based on CPU Cores
        public static void ConfigureAdaptiveThreshold(UpdateManager updateManager)
        {
            int coreCount = Environment.ProcessorCount;
            Console.WriteLine($"Detected {coreCount} CPU cores");
            
            // Adjust threshold based on available cores
            if (coreCount >= 8)
            {
                updateManager.ParallelThreshold = 50;  // More cores = lower threshold
                Console.WriteLine("High core count - using aggressive parallelization");
            }
            else if (coreCount >= 4)
            {
                updateManager.ParallelThreshold = 100; // Default for quad-core
                Console.WriteLine("Moderate core count - using standard parallelization");
            }
            else
            {
                updateManager.ParallelThreshold = 200; // Fewer cores = higher threshold
                Console.WriteLine("Low core count - conservative parallelization");
            }
        }

        // Example 3: Benchmark Parallel vs Sequential
        public static void BenchmarkParallelPerformance(UpdateManager updateManager, GameTime gameTime, int iterations = 100)
        {
            var stopwatch = new Stopwatch();
            
            // Warmup (important for accurate benchmarking)
            for (int i = 0; i < 10; i++)
            {
                updateManager.Update(gameTime);
            }
            
            // Benchmark Sequential
            Console.WriteLine("\n=== Benchmarking Sequential Processing ===");
            updateManager.EnableParallelForces = false;
            stopwatch.Restart();
            
            for (int i = 0; i < iterations; i++)
            {
                updateManager.Update(gameTime);
            }
            
            stopwatch.Stop();
            double sequentialTime = stopwatch.Elapsed.TotalMilliseconds;
            double sequentialAvg = sequentialTime / iterations;
            Console.WriteLine($"Total time: {sequentialTime:F2}ms");
            Console.WriteLine($"Average per update: {sequentialAvg:F2}ms");
            
            // Warmup parallel
            updateManager.EnableParallelForces = true;
            for (int i = 0; i < 10; i++)
            {
                updateManager.Update(gameTime);
            }
            
            // Benchmark Parallel
            Console.WriteLine("\n=== Benchmarking Parallel Processing ===");
            stopwatch.Restart();
            
            for (int i = 0; i < iterations; i++)
            {
                updateManager.Update(gameTime);
            }
            
            stopwatch.Stop();
            double parallelTime = stopwatch.Elapsed.TotalMilliseconds;
            double parallelAvg = parallelTime / iterations;
            Console.WriteLine($"Total time: {parallelTime:F2}ms");
            Console.WriteLine($"Average per update: {parallelAvg:F2}ms");
            
            // Calculate speedup
            double speedup = sequentialTime / parallelTime;
            double efficiency = speedup / Environment.ProcessorCount * 100;
            
            Console.WriteLine("\n=== Results ===");
            Console.WriteLine($"Speedup: {speedup:F2}x");
            Console.WriteLine($"Efficiency: {efficiency:F1}% (on {Environment.ProcessorCount} cores)");
            Console.WriteLine($"Time saved per update: {sequentialAvg - parallelAvg:F2}ms");
            
            if (speedup > 1.5)
            {
                Console.WriteLine("✅ Parallel processing provides significant benefit!");
            }
            else if (speedup > 1.1)
            {
                Console.WriteLine("⚠️ Modest improvement - consider tuning threshold");
            }
            else
            {
                Console.WriteLine("❌ No benefit - try sequential or increase entity count");
            }
        }

        // Example 4: Monitor Performance During Simulation
        public static void MonitorPerformance(UpdateManager updateManager, GameTime gameTime, int entityCount)
        {
            var stopwatch = new Stopwatch();
            bool isParallel = updateManager.EnableParallelForces && entityCount >= updateManager.ParallelThreshold;
            
            stopwatch.Start();
            updateManager.Update(gameTime);
            stopwatch.Stop();
            
            double updateTime = stopwatch.Elapsed.TotalMilliseconds;
            string mode = isParallel ? "PARALLEL" : "SEQUENTIAL";
            
            Console.WriteLine($"[{mode}] Update time: {updateTime:F2}ms ({entityCount} entities)");
            
            // Warn if update is taking too long
            double targetFrameTime = 1000.0 / 60.0; // 60 FPS = 16.67ms per frame
            if (updateTime > targetFrameTime)
            {
                Console.WriteLine($"⚠️ Warning: Update time exceeds target ({targetFrameTime:F2}ms)");
                if (!isParallel && entityCount >= updateManager.ParallelThreshold / 2)
                {
                    Console.WriteLine("💡 Tip: Consider lowering ParallelThreshold to enable parallelization sooner");
                }
            }
        }

        // Example 5: Dynamic Switching Based on Load
        public static void DynamicParallelControl(UpdateManager updateManager, int currentEntityCount, double lastUpdateTime)
        {
            const double targetUpdateTime = 10.0; // Target: 10ms per update
            
            // If updates are slow and we have enough entities, lower threshold
            if (lastUpdateTime > targetUpdateTime && currentEntityCount > 50)
            {
                if (updateManager.ParallelThreshold > 50)
                {
                    updateManager.ParallelThreshold -= 10;
                    Console.WriteLine($"Performance struggling - lowered threshold to {updateManager.ParallelThreshold}");
                }
            }
            
            // If updates are fast and we're using parallelization unnecessarily, raise threshold
            if (lastUpdateTime < targetUpdateTime / 2 && currentEntityCount < updateManager.ParallelThreshold)
            {
                updateManager.ParallelThreshold += 10;
                Console.WriteLine($"Performance good - raised threshold to {updateManager.ParallelThreshold}");
            }
        }

        // Example 6: Profile Different Simulation Sizes
        public static void ProfileSimulationScaling()
        {
            Console.WriteLine("\n=== Parallel Force Scaling Analysis ===");
            Console.WriteLine($"System: {Environment.ProcessorCount} cores, {Environment.Is64BitProcess} bit");
            Console.WriteLine("\nExpected performance characteristics:");
            
            var simulationSizes = new[] { 50, 100, 200, 500, 1000, 2000 };
            
            Console.WriteLine("\nEntity Count | Sequential | Parallel (4c) | Speedup");
            Console.WriteLine("-------------|------------|---------------|--------");
            
            foreach (int size in simulationSizes)
            {
                // O(N²) complexity for force calculations
                double sequentialTime = Math.Pow(size, 2) * 0.00001; // Approximate
                double parallelTime = sequentialTime / Math.Min(Environment.ProcessorCount, 4);
                double speedup = sequentialTime / parallelTime;
                
                Console.WriteLine($"{size,12} | {sequentialTime,9:F2}ms | {parallelTime,12:F2}ms | {speedup,6:F2}x");
            }
            
            Console.WriteLine("\nNote: Actual results depend on force complexity and system architecture");
        }

        // Example 7: Best Practices
        public static void BestPractices()
        {
            Console.WriteLine("\n=== Parallel Force Best Practices ===\n");
            
            Console.WriteLine("✅ DO:");
            Console.WriteLine("  • Enable parallel forces for 100+ entities");
            Console.WriteLine("  • Adjust threshold based on CPU core count");
            Console.WriteLine("  • Benchmark on your target hardware");
            Console.WriteLine("  • Keep entity data read-only during force calculation");
            Console.WriteLine("  • Use for simulations with complex force calculations");
            
            Console.WriteLine("\n❌ DON'T:");
            Console.WriteLine("  • Enable for very small simulations (< 50 entities)");
            Console.WriteLine("  • Modify entity positions during force calculation");
            Console.WriteLine("  • Set threshold too low (increases overhead)");
            Console.WriteLine("  • Assume linear scaling with core count");
            Console.WriteLine("  • Forget to profile on actual hardware");
            
            Console.WriteLine("\n💡 TIPS:");
            Console.WriteLine("  • Start with default threshold (100)");
            Console.WriteLine("  • Monitor update times during development");
            Console.WriteLine("  • Consider disabling for debugging");
            Console.WriteLine("  • Parallel force + sequential collision works well");
            Console.WriteLine("  • Cache locality matters - keep entity data contiguous");
        }
    }

    // Example usage in Game.cs:
    /*
    public class Game : EngineBase
    {
        private readonly UpdateManager _updateManager = new();
        
        protected override void Initialize()
        {
            // Configure parallel processing
            ParallelForceExample.ConfigureAdaptiveThreshold(_updateManager);
            
            // Or manually configure
            _updateManager.EnableParallelForces = true;
            _updateManager.ParallelThreshold = 100;
            
            CreateObjects();
            
            // Benchmark if needed
            ParallelForceExample.BenchmarkParallelPerformance(_updateManager, GameTime, 100);
        }
        
        protected override void Update(GameTime gameTime)
        {
            // Regular update
            _updateManager.Update(gameTime);
            
            // Optional: Monitor performance
            ParallelForceExample.MonitorPerformance(_updateManager, gameTime, _entities.Count);
        }
    }
    */
}
