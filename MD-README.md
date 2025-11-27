# Molecular Dynamics (MD) Attribute System

A full-featured but simple molecular dynamics simulation system for Engine-13.

## Overview

The `MolecularDynamics` component enables entity-based physics simulations with:
- **Single-particle harmonic oscillators** with optional external driving forces
- **Multi-particle interactions** including Lennard-Jones potentials and dynamic bonding
- **Energy conservation** tracking (kinetic, potential, and total energy)
- **Phase transitions** between gas, liquid, and solid-like states

## Quick Start

### Simple Oscillator
```csharp
var entity = new Entity(particleMesh);
var md = MolecularDynamics.CreateSimpleOscillator(
    springConstant: 15f,
    damping: 0.8f
);
entity.AddComponent(md);
```

### Gas Simulation
```csharp
var entities = MDExamples.CreateGasSimulation(
    particleCount: 100,
    particleMesh: mesh,
    boxSize: 1f
);
```

### Liquid Simulation
```csharp
var entities = MDExamples.CreateLiquidSimulation(
    particleCount: 100,
    particleMesh: mesh,
    boxSize: 1f
);
```

### Solid/Crystal Simulation
```csharp
var entities = MDExamples.CreateSolidSimulation(
    gridSize: 10,
    particleMesh: mesh,
    spacing: 0.03f
);
```

## Core Features

### 1. Single-Particle Oscillator
Models a mass-spring-damper system with optional sinusoidal driving force.

**Physics:**
- Spring force: `F = -k * (x - x₀)`
- Damping force: `F = -c * v`
- Driving force: `F = A * sin(ωt + φ)`

**Parameters:**
- `SpringConstant` - Spring stiffness (default: 10)
- `DampingRatio` - 1.0 = critical damping (default: 1.1)
- `DriveAmplitude` - External force amplitude (default: 0.005)
- `DriveFrequency` - Drive frequency in Hz (default: 4)

### 2. Lennard-Jones Potential
Models van der Waals interactions between particles.

**Potential:**
```
U(r) = 4ε[(σ/r)¹² - (σ/r)⁶]
```

**Force:**
```
F(r) = 24ε/r[(σ/r)⁶ - 2(σ/r)¹²]
```

**Parameters:**
- `LJ_Epsilon` - Energy scale (depth of potential well)
- `LJ_Sigma` - Length scale (particle diameter)
- `LJ_CutoffRadius` - Maximum interaction distance

### 3. Dynamic Bonding
Particles automatically form bonds when within cutoff distance.

**Bond Force:**
```
F = k * (r - r₀)
```

**Parameters:**
- `BondSpringConstant` - Bond stiffness
- `BondEquilibriumLength` - Natural bond length
- `BondCutoffDistance` - Max distance for bond formation
- `MaxBondsPerEntity` - Maximum bonds per particle

## Preset Configurations

### CreateSimpleOscillator()
Basic harmonic oscillator (no interactions)
- **Use case:** Single particle physics, resonance demos

### CreateDrivenOscillator()
Oscillator with external forcing
- **Use case:** Resonance, forced vibration studies

### CreateGasParticle()
Lennard-Jones only (no bonds)
- **Use case:** Gas simulations, kinetic theory

### CreateLiquidParticle()
LJ + weak bonding (4 bonds max)
- **Use case:** Liquid behavior, clustering

### CreateSolidParticle()
LJ + strong bonding (6 bonds max)
- **Use case:** Crystals, rigid structures

### CreateFullMD()
All features enabled
- **Use case:** Complex simulations, phase transitions

## System Analysis Tools

### Energy Calculations
```csharp
float ke = MolecularDynamics.CalculateKineticEnergy(entities);
float pe = MolecularDynamics.CalculatePotentialEnergy(entities);
float total = MolecularDynamics.CalculateTotalEnergy(entities);
```

### Temperature
```csharp
float temp = MolecularDynamics.CalculateTemperature(entities);
```

### Bond Information
```csharp
int bondCount = MolecularDynamics.GetBondCount();
MolecularDynamics.ClearAllBonds(); // Reset all bonds
```

## Parameter Guidelines

### Spring Constants
- **Very Soft:** 1-5 (slow oscillation)
- **Soft:** 5-15 (medium oscillation)
- **Stiff:** 15-50 (fast oscillation)
- **Very Stiff:** 50+ (rigid-like)

### Damping Ratio
- **Underdamped:** < 1.0 (oscillates before settling)
- **Critical:** 1.0 (fastest settling without oscillation)
- **Overdamped:** > 1.0 (slow approach to equilibrium)

### Lennard-Jones
- **ε (Epsilon):** Controls attraction strength (typical: 0.001-0.01)
- **σ (Sigma):** Particle size (typical: 0.01-0.05)
- **Cutoff:** 2.5σ to 4σ for efficiency

## Performance Tips

1. **Limit interaction range** - Use smaller cutoff radii
2. **Cap max forces** - Increase `MaxForceMagnitude` carefully
3. **Reduce particle count** - Start small, scale up
4. **Use spatial partitioning** - For >100 particles, implement grid-based neighbor finding
5. **Disable unused features** - Turn off bonds or LJ if not needed

## Common Use Cases

### 1. Resonance Demo
```csharp
var md = MolecularDynamics.CreateDrivenOscillator(
    driveAmplitude: 0.01f,
    driveFrequency: 2f // Match natural frequency for resonance
);
```

### 2. Phase Transition
```csharp
// Start with solid
var entities = MDExamples.CreateSolidSimulation(10, mesh);

// Heat system (add kinetic energy)
foreach (var entity in entities)
{
    entity.Velocity += new Vector2(randomX, randomY) * 0.1f;
}

// Watch bonds break as temperature increases
```

### 3. Molecule Formation
```csharp
// Start with gas
var entities = MDExamples.CreateGasSimulation(50, mesh);

// Cool system (remove kinetic energy)
foreach (var entity in entities)
{
    entity.Velocity *= 0.99f; // Gradual cooling
}

// Watch bonds form and clusters appear
```

## Physics Equations Reference

### Harmonic Oscillator
- **Equation of motion:** `m * a = -k * x - c * v + F_drive`
- **Natural frequency:** `ω₀ = √(k/m)`
- **Damping coefficient:** `c = 2ζ√(km)`

### Lennard-Jones
- **Equilibrium distance:** `r_eq = 2^(1/6) * σ ≈ 1.122σ`
- **Potential minimum:** `U(r_eq) = -ε`

### Energy Conservation
- **Total energy:** `E = KE + PE`
- **Should be conserved** (may drift slightly due to numerical integration)

## Troubleshooting

### Particles explode/unstable
- **Reduce** `MaxForceMagnitude`
- **Increase** damping
- **Lower** spring constants
- **Reduce** timestep

### No interactions visible
- **Check** `EnableInteractions` is true
- **Verify** particles are within cutoff distances
- **Ensure** `_allEntities` list is passed to constructor

### Bonds not forming
- **Check** `EnableBonds` is true
- **Reduce** `BondCutoffDistance`
- **Verify** particles get close enough
- **Check** `MaxBondsPerEntity` isn't 0

### Energy not conserved
- **Normal:** Some drift expected with simple integration
- **Reduce timestep** for better conservation
- **Check** for external forces (gravity, etc.)

## Examples

See `MDExamples.cs` for complete working examples including:
- All preset configurations
- Custom parameter tuning
- Phase transition demos
- Energy monitoring
- System reset utilities

## Integration with Other Components

### With Gravity
```csharp
entity.AddComponent(new Gravity(9.8f));
entity.AddComponent(MolecularDynamics.CreateGasParticle(entities));
// Particles will have both MD forces and gravity
```

### With EdgeCollision
```csharp
entity.AddComponent(new EdgeCollision(loop: false));
entity.AddComponent(MolecularDynamics.CreateLiquidParticle(entities));
// Particles contained in boundary with wall collisions
```

### With ObjectCollision
```csharp
entity.AddComponent(new ObjectCollision { Mass = 2f, Restitution = 0.9f });
entity.AddComponent(MolecularDynamics.CreateSolidParticle(entities));
// Particles with elastic collisions and MD forces
```

## License

Part of Engine-13 game engine.
