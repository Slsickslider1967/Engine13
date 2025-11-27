using System;
using System.Collections.Generic;
using System.Numerics;
using Engine13.Core;
using Engine13.Graphics;
using Engine13.Utilities.Attributes;

namespace Engine13.Utilities.Examples
{
    /// <summary>
    /// Example usage patterns for the MolecularDynamics attribute system
    /// Demonstrates various simulation types from simple oscillators to full MD
    /// </summary>
    public static class MDExamples
    {
        #region Simple Oscillator Examples

        /// <summary>
        /// Example 1: Simple Harmonic Oscillator
        /// A particle oscillating around a fixed point with spring-like behavior
        /// </summary>
        public static void CreateSimpleOscillator(Entity entity)
        {
            // Create a basic oscillator with medium spring constant
            var md = MolecularDynamics.CreateSimpleOscillator(
                springConstant: 15f, // Stiffer spring = faster oscillation
                damping: 0.8f // Underdamped = oscillates before settling
            );

            entity.AddComponent(md);
        }

        /// <summary>
        /// Example 2: Driven Oscillator (Resonance Demo)
        /// Shows resonance behavior when driven at natural frequency
        /// </summary>
        public static void CreateDrivenOscillator(Entity entity)
        {
            var md = MolecularDynamics.CreateDrivenOscillator(
                driveAmplitude: 0.01f, // How much force to apply
                driveFrequency: 2f // Drive frequency in Hz
            );

            entity.AddComponent(md);
        }

        #endregion

        #region Multi-Particle Simulation Examples

        /// <summary>
        /// Example 3: Gas Simulation
        /// Particles interact via Lennard-Jones potential only (no bonds)
        /// Simulates gas-like behavior with free particle motion
        /// </summary>
        public static List<Entity> CreateGasSimulation(
            int particleCount,
            RenderMesh particleMesh,
            float boxSize = 1f
        )
        {
            var entities = new List<Entity>();
            var random = new Random();

            // Create all particles first
            for (int i = 0; i < particleCount; i++)
            {
                var entity = new Entity(particleMesh);

                // Random positions in box
                entity.Position = new Vector2(
                    (float)(random.NextDouble() * boxSize - boxSize / 2),
                    (float)(random.NextDouble() * boxSize - boxSize / 2)
                );

                // Random velocities (temperature control)
                entity.Velocity = new Vector2(
                    (float)(random.NextDouble() * 0.2f - 0.1f),
                    (float)(random.NextDouble() * 0.2f - 0.1f)
                );

                entity.Mass = 1f;
                entities.Add(entity);
            }

            // Add MD component to each particle
            foreach (var entity in entities)
            {
                var md = MolecularDynamics.CreateGasParticle(entities);
                entity.AddComponent(md);
            }

            return entities;
        }

        /// <summary>
        /// Example 4: Liquid Simulation
        /// Particles can form weak bonds and have LJ interactions
        /// Simulates liquid-like behavior with clustering
        /// </summary>
        public static List<Entity> CreateLiquidSimulation(
            int particleCount,
            RenderMesh particleMesh,
            float boxSize = 1f
        )
        {
            var entities = new List<Entity>();
            var random = new Random();

            for (int i = 0; i < particleCount; i++)
            {
                var entity = new Entity(particleMesh);
                entity.Position = new Vector2(
                    (float)(random.NextDouble() * boxSize - boxSize / 2),
                    (float)(random.NextDouble() * boxSize - boxSize / 2)
                );
                entity.Velocity = new Vector2(
                    (float)(random.NextDouble() * 0.1f - 0.05f),
                    (float)(random.NextDouble() * 0.1f - 0.05f)
                );
                entity.Mass = 1f;
                entities.Add(entity);
            }

            foreach (var entity in entities)
            {
                var md = MolecularDynamics.CreateLiquidParticle(entities);
                entity.AddComponent(md);
            }

            return entities;
        }

        /// <summary>
        /// Example 5: Solid/Crystal Simulation
        /// Particles form strong bonds creating rigid structure
        /// </summary>
        public static List<Entity> CreateSolidSimulation(
            int gridSize,
            RenderMesh particleMesh,
            float spacing = 0.03f
        )
        {
            var entities = new List<Entity>();

            // Create particles in a grid pattern
            for (int i = 0; i < gridSize; i++)
            {
                for (int j = 0; j < gridSize; j++)
                {
                    var entity = new Entity(particleMesh);
                    entity.Position = new Vector2(
                        (i - gridSize / 2f) * spacing,
                        (j - gridSize / 2f) * spacing
                    );
                    entity.Velocity = Vector2.Zero;
                    entity.Mass = 1f;
                    entities.Add(entity);
                }
            }

            foreach (var entity in entities)
            {
                var md = MolecularDynamics.CreateSolidParticle(entities);
                entity.AddComponent(md);
            }

            return entities;
        }

        #endregion

        #region Custom Configuration Examples

        /// <summary>
        /// Example 6: Custom MD Configuration
        /// Shows how to manually configure all parameters
        /// </summary>
        public static void CreateCustomMD(Entity entity, List<Entity> allEntities)
        {
            var md = new MolecularDynamics(allEntities)
            {
                // Single-particle oscillator settings
                EnableAnchorOscillation = true,
                SpringConstant = 20f,
                DampingRatio = 0.5f,
                EnableDrive = true,
                DriveAmplitude = 0.008f,
                DriveFrequency = 3f,

                // Multi-particle interaction settings
                EnableInteractions = true,
                EnableBonds = true,
                EnableLennardJones = true,

                // Bond parameters
                MaxBondsPerEntity = 4,
                BondSpringConstant = 60f,
                BondEquilibriumLength = 0.03f,
                BondCutoffDistance = 0.04f,

                // Lennard-Jones parameters
                LJ_Epsilon = 0.01f, // Energy scale
                LJ_Sigma = 0.025f, // Length scale
                LJ_CutoffRadius = 0.1f,

                // Stability
                MaxForceMagnitude = 30f,
            };

            entity.AddComponent(md);
        }

        /// <summary>
        /// Example 7: Phase Transition Demo
        /// Gradually change parameters to see phase changes (solid -> liquid -> gas)
        /// </summary>
        public static void ConfigureForPhaseTransition(
            List<Entity> entities,
            float temperature
        )
        {
            foreach (var entity in entities)
            {
                var md = entity.GetComponent<MolecularDynamics>();
                if (md == null)
                    continue;

                // Adjust parameters based on "temperature"
                if (temperature < 0.3f) // Solid phase
                {
                    md.EnableBonds = true;
                    md.MaxBondsPerEntity = 6;
                    md.BondSpringConstant = 100f;
                }
                else if (temperature < 0.7f) // Liquid phase
                {
                    md.EnableBonds = true;
                    md.MaxBondsPerEntity = 3;
                    md.BondSpringConstant = 40f;
                }
                else // Gas phase
                {
                    md.EnableBonds = false;
                    md.EnableLennardJones = true;
                }
            }
        }

        #endregion

        #region Analysis Examples

        /// <summary>
        /// Example 8: Energy Monitoring
        /// Track and display system energy over time
        /// </summary>
        public static void MonitorSystemEnergy(List<Entity> entities, GameTime gameTime)
        {
            // Calculate energies
            float kineticEnergy = MolecularDynamics.CalculateKineticEnergy(entities);
            float potentialEnergy = MolecularDynamics.CalculatePotentialEnergy(entities);
            float totalEnergy = MolecularDynamics.CalculateTotalEnergy(entities);
            float temperature = MolecularDynamics.CalculateTemperature(entities);
            int bondCount = MolecularDynamics.GetBondCount();

            // Log or display results
            Logger.Log($"Time: {gameTime.TotalTime:F2}s");
            Logger.Log($"KE: {kineticEnergy:F4}, PE: {potentialEnergy:F4}, Total: {totalEnergy:F4}");
            Logger.Log($"Temperature: {temperature:F4}, Bonds: {bondCount}");
        }

        /// <summary>
        /// Example 9: System Reset
        /// Clear all bonds and reinitialize system
        /// </summary>
        public static void ResetSimulation(List<Entity> entities)
        {
            // Clear global bond information
            MolecularDynamics.ClearAllBonds();

            // Reset particle positions and velocities
            var random = new Random();
            foreach (var entity in entities)
            {
                entity.Position = new Vector2(
                    (float)(random.NextDouble() * 2f - 1f),
                    (float)(random.NextDouble() * 2f - 1f)
                );
                entity.Velocity = Vector2.Zero;
            }
        }

        #endregion

        #region Quick Start Template

        /// <summary>
        /// Quick start: Complete MD simulation setup
        /// Use this as a starting point for your own simulations
        /// </summary>
        public static List<Entity> QuickStartMDSimulation(
            RenderMesh particleMesh,
            int particleCount = 50,
            SimulationType simType = SimulationType.Liquid
        )
        {
            List<Entity> entities;

            switch (simType)
            {
                case SimulationType.Gas:
                    entities = CreateGasSimulation(particleCount, particleMesh);
                    break;

                case SimulationType.Liquid:
                    entities = CreateLiquidSimulation(particleCount, particleMesh);
                    break;

                case SimulationType.Solid:
                    int gridSize = (int)Math.Sqrt(particleCount);
                    entities = CreateSolidSimulation(gridSize, particleMesh);
                    break;

                default:
                    entities = CreateLiquidSimulation(particleCount, particleMesh);
                    break;
            }

            return entities;
        }

        public enum SimulationType
        {
            Gas,
            Liquid,
            Solid,
        }

        #endregion
    }
}
