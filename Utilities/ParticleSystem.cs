using System.Linq;
using System.Numerics;
using Engine13.Graphics;
using Engine13.Primitives;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;
using Engine13.Utilities.JsonReader;
using Veldrid;

namespace Engine13.Core
{
    public class FluidParticle
    {
        public Entity Entity { get; set; } = null!;
        public List<FluidParticle> Neighbors { get; } = new();
    }

    public class ParticleSystem
    {
        public string Name { get; }
        public ParticlePresetReader Material { get; }
        public List<Entity> Particles { get; } = new();

        readonly List<FluidParticle> _fluidParticles = new();
        readonly Dictionary<Entity, FluidParticle> _fluidMap = new();

        float _smoothingRadius;
        float _stiffness;
        float _viscosity;
        float _restDensity;
        float _surfaceTension;
        float[]? _densities;
        float[]? _pressures;
        Vector2[]? _forces;
        float[]? _colorField;
        Vector2[]? _gradColor;
        float[]? _lapColor;
        float _particleRadius;
        float _maxVelocity = 2f;
        float _damping = 0.95f;
        Vector2 _gravity;
        readonly List<Entity> _neighborEntityBuffer = new();

        public ParticleSystem(string name, ParticlePresetReader material)
        {
            Name = name;
            Material = material;
        }

        public ParticleSystem(string name, string presetName)
            : this(name, ParticlePresetReader.Load(presetName)) { }

        public void CreateParticles(
            GraphicsDevice graphicsDevice,
            int particleCount,
            Vector2 origin,
            int columns,
            List<Entity> allEntities,
            UpdateManager updateManager,
            SpatialGrid grid
        )
        {
            float particleRadius = Material.ParticleRadius;
            float diameter = particleRadius * 2f;
            float horizontalSpacing = diameter * 1.15f;
            float verticalSpacing = diameter * 1.15f;

            Particles.Clear();
            Particles.Capacity = particleCount;

            if (Material.Composition != null && Material.Composition.Count > 0)
            {
                CreateCompositionParticles(
                    graphicsDevice,
                    particleCount,
                    origin,
                    columns,
                    horizontalSpacing,
                    verticalSpacing,
                    particleRadius,
                    allEntities,
                    updateManager,
                    grid
                );
            }
            else
            {
                CreateUniformParticles(
                    graphicsDevice,
                    particleCount,
                    origin,
                    columns,
                    horizontalSpacing,
                    verticalSpacing,
                    particleRadius,
                    allEntities,
                    updateManager,
                    grid
                );
            }

            Logger.Log(
                $"[{Name}] Created {Particles.Count} particles with material '{Material.Name}'"
            );
            Logger.Log(
                $"  Material: restitution={Material.Restitution:F2}, friction={Material.Friction:F2}, mass={Material.Mass:F4}"
            );
        }

        private void CreateUniformParticles(
            GraphicsDevice graphicsDevice,
            int particleCount,
            Vector2 origin,
            int columns,
            float horizontalSpacing,
            float verticalSpacing,
            float particleRadius,
            List<Entity> allEntities,
            UpdateManager updateManager,
            SpatialGrid grid
        )
        {
            for (int i = 0; i < particleCount; i++)
            {
                var particle = CreateSingleParticle(
                    graphicsDevice,
                    i,
                    origin,
                    columns,
                    horizontalSpacing,
                    verticalSpacing,
                    particleRadius,
                    "standard",
                    allEntities,
                    grid
                );

                RegisterParticle(particle, updateManager, grid, allEntities);
            }
        }

        private void CreateCompositionParticles(
            GraphicsDevice graphicsDevice,
            int particleCount,
            Vector2 origin,
            int columns,
            float horizontalSpacing,
            float verticalSpacing,
            float particleRadius,
            List<Entity> allEntities,
            UpdateManager updateManager,
            SpatialGrid grid
        )
        {
            var composition = Material.Composition!;
            int totalRatio = composition.Sum(c => Math.Max(1, c.Ratio));
            var compCounts = new List<int>(composition.Count);
            int remaining = particleCount;

            for (int ci = 0; ci < composition.Count; ci++)
            {
                var c = composition[ci];
                int count =
                    (ci == composition.Count - 1)
                        ? remaining
                        : (int)Math.Round((double)particleCount * c.Ratio / totalRatio);
                compCounts.Add(Math.Max(0, count));
                remaining -= count;
            }

            int index = 0;
            for (int ci = 0; ci < composition.Count; ci++)
            {
                var comp = composition[ci];
                int compCount = compCounts[ci];

                for (int k = 0; k < compCount; k++, index++)
                {
                    string particleType = comp.ParticleType?.ToLowerInvariant() ?? "standard";
                    var particle = CreateSingleParticle(
                        graphicsDevice,
                        index,
                        origin,
                        columns,
                        horizontalSpacing,
                        verticalSpacing,
                        particleRadius,
                        particleType,
                        allEntities,
                        grid
                    );

                    RegisterParticle(particle, updateManager, grid, allEntities);
                }
            }
        }

        private Entity CreateSingleParticle(
            GraphicsDevice graphicsDevice,
            int index,
            Vector2 origin,
            int columns,
            float horizontalSpacing,
            float verticalSpacing,
            float particleRadius,
            string particleType,
            List<Entity> allEntities,
            SpatialGrid grid
        )
        {
            var particle = CircleFactory.CreateCircle(graphicsDevice, particleRadius, 8, 8);

            int column = index % columns;
            int row = index / columns;
            particle.Position = new Vector2(
                origin.X + column * horizontalSpacing,
                origin.Y + row * verticalSpacing
            );

            particle.CollisionRadius = particleRadius;
            particle.Mass = Material.Mass;

            particle.AddComponent(
                new Gravity(
                    Material.GravityStrength,
                    0f,
                    particle.Mass,
                    Material.HorizontalForce,
                    0f
                )
            );
            particle.AddComponent(
                new ObjectCollision
                {
                    Mass = particle.Mass,
                    Restitution = Material.Restitution,
                    Friction = Material.Friction,
                    IsFluid = Material.IsFluid,
                }
            );
            particle.AddComponent(
                Material.EnableEdgeCollision ? new EdgeCollision(false) : new EdgeCollision(true)
            );

            var particleDynamics = new ParticleDynamics(allEntities, grid);
            switch (particleType)
            {
                case "heavy":
                    particleDynamics.MaxForceMagnitude = 100f;
                    particleDynamics.VelocityDamping = 0.01f;
                    particleDynamics.PressureStrength = 4f;
                    particleDynamics.PressureRadius = ParticleDynamics.DefaultPressureRadius;
                    break;
                case "light":
                    particleDynamics.MaxForceMagnitude = 25f;
                    particleDynamics.VelocityDamping = 0.05f;
                    particleDynamics.PressureStrength = 2f;
                    particleDynamics.PressureRadius = ParticleDynamics.DefaultPressureRadius;
                    break;
                case "fluid":
                    particleDynamics.MaxForceMagnitude = 15f;
                    particleDynamics.VelocityDamping = 0.08f;
                    particleDynamics.PressureStrength = 1.5f;
                    particleDynamics.PressureRadius = ParticleDynamics.DefaultPressureRadius;
                    break;
                default:
                    particleDynamics.MaxForceMagnitude = 50f;
                    particleDynamics.VelocityDamping = 0.02f;
                    particleDynamics.PressureStrength = 2.5f;
                    particleDynamics.PressureRadius = ParticleDynamics.DefaultPressureRadius;
                    break;
            }

            Material.ApplyTo(particleDynamics);
            particle.AddComponent(particleDynamics);

            return particle;
        }

        private void RegisterParticle(
            Entity particle,
            UpdateManager updateManager,
            SpatialGrid grid,
            List<Entity> allEntities
        )
        {
            Particles.Add(particle);
            allEntities.Add(particle);
            updateManager.Register(particle);
            grid.AddEntity(particle);
        }

        public void InitializeFluid()
        {
            if (!Material.IsFluid)
                return;

            _smoothingRadius = Material.PressureRadius * 2f;
            _stiffness = Material.SPHGasConstant;
            _viscosity = Material.SPHViscosity;
            _restDensity = Material.SPHRestDensity;
            _surfaceTension = Material.SPHSurfaceTension;
            _particleRadius = Material.ParticleRadius;
            _gravity = new Vector2(0f, Material.GravityStrength);
            _damping = 1f - (Material.VelocityDamping * 0.25f);

            _fluidParticles.Clear();
            _fluidMap.Clear();

            foreach (var particle in Particles)
            {
                var fp = new FluidParticle { Entity = particle };
                _fluidParticles.Add(fp);
                _fluidMap[particle] = fp;

                var pd = particle.GetComponent<ParticleDynamics>();
                if (pd != null)
                    pd.UseSPHSolver = true;

                var oc = particle.GetComponent<ObjectCollision>();
                if (oc != null)
                    oc.UseSPHIntegration = true;
            }

            int n = _fluidParticles.Count;
            _densities = new float[n];
            _pressures = new float[n];
            _forces = new Vector2[n];
            _colorField = new float[n];
            _gradColor = new Vector2[n];
            _lapColor = new float[n];
        }

        public void StepFluid(float dt, SpatialGrid grid)
        {
            if (!Material.IsFluid || _fluidParticles.Count == 0)
                return;

            float h = _smoothingRadius;
            float h2 = h * h;
            int n = _fluidParticles.Count;

            // Ensure buffers
            if (_densities == null || _densities.Length < n)
            {
                _densities = new float[n];
                _pressures = new float[n];
                _forces = new Vector2[n];
                _colorField = new float[n];
                _gradColor = new Vector2[n];
                _lapColor = new float[n];
            }

            // Neighbor build (no-alloc)
            for (int i = 0; i < n; i++)
            {
                var p = _fluidParticles[i];
                p.Neighbors.Clear();
                grid.GetNearbyEntities(p.Entity.Position, _neighborEntityBuffer);
                foreach (var other in _neighborEntityBuffer)
                {
                    if (other == p.Entity)
                        continue;
                    if (!_fluidMap.TryGetValue(other, out var fp))
                        continue;
                    if (Vector2.DistanceSquared(p.Entity.Position, fp.Entity.Position) < h2)
                        p.Neighbors.Add(fp);
                }
            }

            // Density pass
            for (int i = 0; i < n; i++)
            {
                _densities[i] = 0f;
                var pi = _fluidParticles[i];
                // self contribution
                float mi = pi.Entity.Mass > 0f ? pi.Entity.Mass : 1f;
                _densities[i] += mi * SphKernels.Poly6(0f, h);

                foreach (var nb in pi.Neighbors)
                {
                    float mj = nb.Entity.Mass > 0f ? nb.Entity.Mass : 1f;
                    float r = Vector2.Distance(pi.Entity.Position, nb.Entity.Position);
                    _densities[i] += mj * SphKernels.Poly6(r, h);
                }
            }

            // Pressure
            for (int i = 0; i < n; i++)
            {
                float rho = MathF.Max(1e-6f, _densities[i]);
                _pressures[i] = _stiffness * MathF.Max(0f, rho - _restDensity);
                _forces[i] = Vector2.Zero;
                _colorField![i] = 0f;
                _gradColor![i] = Vector2.Zero;
                _lapColor![i] = 0f;
            }

            // Force pass
            for (int i = 0; i < n; i++)
            {
                var pi = _fluidParticles[i];
                var oc_i = pi.Entity.GetComponent<ObjectCollision>();
                if (oc_i == null || oc_i.IsStatic)
                    continue;

                Vector2 pressureForce = Vector2.Zero;
                Vector2 viscosityForce = Vector2.Zero;

                float rho_i = MathF.Max(1e-6f, _densities[i]);
                float p_i = _pressures[i];

                // include self in color field
                float mi = pi.Entity.Mass > 0f ? pi.Entity.Mass : 1f;
                _colorField![i] += mi / rho_i * SphKernels.Poly6(0f, h);

                foreach (var nb in pi.Neighbors)
                {
                    int j = _fluidParticles.IndexOf(nb);
                    if (j < 0)
                        continue;

                    var pj = nb.Entity;
                    var oc_j = pj.GetComponent<ObjectCollision>();

                    float mj = pj.Mass > 0f ? pj.Mass : 1f;
                    Vector2 rij = pi.Entity.Position - pj.Position;
                    float r = rij.Length();
                    if (r < 1e-6f)
                        r = 1e-6f;
                    Vector2 dir = rij / r;

                    // pressure
                    Vector2 gradW = SphKernels.SpikyGradient(r, h, dir);
                    float rho_j = MathF.Max(1e-6f, _densities[j]);
                    float p_j = _pressures[j];
                    Vector2 presTerm = -(mj * (p_i + p_j) / (2f * rho_j)) * gradW;
                    pressureForce += presTerm;

                    // viscosity
                    if (oc_j != null)
                    {
                        float lap = SphKernels.ViscosityLaplacian(r, h);
                        viscosityForce +=
                            _viscosity * mj * (oc_j.Velocity - oc_i.Velocity) / rho_j * lap;
                    }

                    // color field for surface tension
                    float W = SphKernels.Poly6(r, h);
                    _colorField[i] += mj / rho_j * W;
                    _gradColor[i] += (mj / rho_j) * SphKernels.SpikyGradient(r, h, dir);
                    _lapColor[i] += mj / rho_j * SphKernels.ViscosityLaplacian(r, h);
                }

                // surface tension
                Vector2 surfaceForce = Vector2.Zero;
                float gradMag = _gradColor[i].Length();
                if (gradMag > 1e-6f)
                {
                    surfaceForce = -_surfaceTension * _lapColor[i] * (_gradColor[i] / gradMag);
                }

                // total force
                Vector2 totalForce =
                    pressureForce + viscosityForce + surfaceForce + (mi * _gravity);
                _forces[i] = totalForce;
            }

            // Integrate
            for (int i = 0; i < n; i++)
            {
                var pi = _fluidParticles[i];
                var oc = pi.Entity.GetComponent<ObjectCollision>();
                if (oc == null || oc.IsStatic)
                    continue;

                float rho = MathF.Max(1e-6f, _densities[i]);
                Vector2 accel = _forces[i] / rho;
                oc.Velocity += accel * dt;

                // damping, clamp
                oc.Velocity *= _damping;
                float speed = oc.Velocity.Length();
                if (speed > _maxVelocity)
                    oc.Velocity *= _maxVelocity / speed;

                pi.Entity.Position += oc.Velocity * dt;
            }
        }
    }

    public static class ParticleSystemFactory
    {
        public static ParticleSystem Create(string systemName, string presetName)
        {
            return new ParticleSystem(systemName, presetName);
        }

        public static List<ParticleSystem> CreateMultiple(
            params (string systemName, string presetName)[] systems
        )
        {
            var result = new List<ParticleSystem>(systems.Length);
            foreach (var (name, preset) in systems)
            {
                result.Add(new ParticleSystem(name, preset));
            }
            return result;
        }
    }
}
