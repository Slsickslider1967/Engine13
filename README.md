# Engine-13

**A High-Performance Particle Physics Engine for Fluid and Granular Material Simulation**

Engine-13 is an advanced experimental 2D particle physics engine developed as a Year 13 school project. It implements state-of-the-art computational fluid dynamics (CFD) and molecular dynamics algorithms, including Smoothed Particle Hydrodynamics (SPH), impulse-based collision resolution, and spring-damper systems. The engine provides a complete rendering pipeline using Veldrid, an interactive ImGui debug interface, and support for both fluid and granular material simulations.

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Features](#features)
3. [Project Structure](#project-structure)
4. [Physics & Simulation](#physics--simulation)
5. [Mathematical Foundations & Equations](#mathematical-foundations--equations)
6. [Configuration & Presets](#configuration--presets)
7. [Developer Notes](#developer-notes)
8. [Known Issues](#known-issues)
9. [Contributing](#contributing)
10. [References](#references)

---

## Quick Start

### Prerequisites
- .NET SDK 9.0 or 10.0
- Compatible OS: Windows, Linux (primary development), macOS

### Build & Run

```bash
# Build the solution
dotnet build Engine-13.sln -c Debug

# Run the engine
dotnet run --project Engine13/Engine-13/Engine-13.csproj -c Debug
```

The compiled executable will be located in `Engine13/Engine-13/bin/Debug/net10.0/` (or `net9.0`).

---

## Features

### Core Capabilities
- **SPH Fluid Simulation**: Weakly Compressible SPH (WCSPH) with Tait equation of state
- **Granular Material Simulation**: Mohr-Coulomb plasticity model with friction and dilatancy
- **Molecular Dynamics**: Spring-damper bonds with Hooke's law
- **Advanced Collision Resolution**: Impulse-based solver with Coulomb friction
- **Spatial Partitioning**: Optimized spatial grid for O(n) neighbor search
- **Real-time Visualization**: Hardware-accelerated rendering with Veldrid
- **Interactive Debug UI**: ImGui interface for parameter tuning and diagnostics

### Simulation Types
- **Fluid Materials**: Water, oil, and other viscous fluids
- **Granular Materials**: Sand, soil, and particulate matter
- **Elastic Materials**: Spring-bonded deformable solids
- **Multi-material Interactions**: Mixed-phase particle systems

---

## Project Structure

```
Engine13/
├── Core/                    # Game loop and entity management
│   ├── EngineBase.cs       # Core engine lifecycle
│   ├── Game.cs             # Main simulation orchestration
│   ├── GameTime.cs         # Time management
│   ├── UpdateManager.cs    # Update scheduling
│   └── ThreadManager.cs    # Parallel processing
├── Graphics/               # Rendering pipeline
│   ├── Renderer.cs         # Main renderer
│   ├── Mesh.cs            # Geometry management
│   └── Sprite.cs          # 2D rendering
├── Utilities/             # Physics and math libraries
│   ├── SPHSolver.cs       # SPH fluid dynamics
│   ├── MolecularDynamics.cs # Bond forces
│   ├── Forces.cs          # Force accumulation
│   ├── MathHelpers.cs     # Collision detection & resolution
│   ├── ParticleSystem.cs  # Particle management
│   └── PhysicsSettings.cs # Global physics parameters
├── Input/                 # Input handling
├── UI/                    # ImGui interface
└── Engine-13/            # Main executable project
    └── Shaders/          # GLSL vertex/fragment shaders
```

---

## Physics & Simulation

### Architecture

Engine-13 uses a **force-based integration scheme** with semi-implicit Euler integration for numerical stability. The simulation pipeline follows this structure:

1. **Neighbor Search**: Spatial grid partitioning
2. **Force Computation**: SPH, bonds, gravity, user forces
3. **Force Integration**: Semi-implicit Euler time integration
4. **Collision Detection**: Spatial grid broad phase + SAT narrow phase
5. **Collision Resolution**: Sequential impulse solver
6. **Position Update**: Apply velocities and constraints

---

## Mathematical Foundations & Equations

This section documents all physics equations implemented in Engine-13, their mathematical foundations, and academic references.

### 1. Smoothed Particle Hydrodynamics (SPH)

SPH is a meshless Lagrangian method for simulating fluid dynamics by approximating continuous fields using kernel functions.

#### 1.1 Kernel Functions

**Poly6 Kernel** - Used for density computation:

$$W_{\text{poly6}}(r, h) = \frac{4}{\pi h^8} \begin{cases} (h^2 - r^2)^3 & \text{if } 0 \leq r < h \\\\ 0 & \text{otherwise} \end{cases}$$

- **Purpose**: Smooth density estimation across particle neighborhoods
- **Implementation**: `SPHKernels.Poly6()` in [SPHSolver.cs](Engine13/Utilities/SPHSolver.cs#L17-L23)
- **Reference**: Müller et al. (2003) - "Particle-Based Fluid Simulation for Interactive Applications"

**Spiky Gradient Kernel** - Used for pressure force computation:

$$\nabla W_{\text{spiky}}(r, h) = -\frac{10}{\pi h^5}(h - r)^2 \frac{\mathbf{r}}{r}$$

- **Purpose**: Asymmetric gradient prevents particle clustering
- **Implementation**: `SPHKernels.SpikyGradient()` in [SPHSolver.cs](Engine13/Utilities/SPHSolver.cs#L25-L31)
- **Reference**: Desbrun & Gascuel (1996) - "Smoothed Particles: A New Paradigm for Animating Highly Deformable Bodies"

**Viscosity Laplacian Kernel** - Used for viscosity forces:

$$\nabla^2 W_{\text{viscosity}}(r, h) = \frac{10}{\pi h^5}(h - r)$$

- **Purpose**: Diffuses velocity differences between particles
- **Implementation**: `SPHKernels.ViscosityLaplacian()` in [SPHSolver.cs](Engine13/Utilities/SPHSolver.cs#L33-L38)
- **Reference**: Müller et al. (2003)

#### 1.2 Density Estimation

$$\rho_i = \sum_j m_j W(|\mathbf{r}_i - \mathbf{r}_j|, h)$$

Where:
- $\rho_i$ = density at particle $i$
- $m_j$ = mass of particle $j$
- $\mathbf{r}_i, \mathbf{r}_j$ = positions of particles $i$ and $j$
- $h$ = smoothing radius (kernel support)

**Implementation**: `ComputeDensities()` in [SPHSolver.cs](Engine13/Utilities/SPHSolver.cs#L120-L136)

#### 1.3 Pressure Computation (Tait Equation of State)

$$p_i = k \left(\frac{\rho_i}{\rho_0} - 1\right)$$

Where:
- $p_i$ = pressure at particle $i$
- $k$ = gas constant (stiffness parameter)
- $\rho_0$ = rest density
- This is a simplified Tait equation for weakly compressible fluids

**Implementation**: `ComputePressures()` in [SPHSolver.cs](Engine13/Utilities/SPHSolver.cs#L138-L143)

**Reference**: Becker & Teschner (2007) - "Weakly compressible SPH for free surface flows"

#### 1.4 Pressure Force (Symmetric Formulation)

$$\mathbf{f}_i^{\text{pressure}} = -m_i \sum_j m_j \left(\frac{p_i}{\rho_i^2} + \frac{p_j}{\rho_j^2}\right) \nabla W_{ij}$$

- **Purpose**: Ensures momentum conservation (Newton's third law)
- **Implementation**: [SPHSolver.cs](Engine13/Utilities/SPHSolver.cs#L207-L212)
- **Reference**: Monaghan (1992) - "Smoothed Particle Hydrodynamics"

#### 1.5 Viscosity Force

$$\mathbf{f}_i^{\text{viscosity}} = \mu \sum_j m_j \frac{\mathbf{v}_j - \mathbf{v}_i}{\rho_j} \nabla^2 W_{ij}$$

Where:
- $\mu$ = dynamic viscosity coefficient
- $\mathbf{v}_i, \mathbf{v}_j$ = velocities of particles

**Implementation**: [SPHSolver.cs](Engine13/Utilities/SPHSolver.cs#L226-L229)

**Reference**: Morris et al. (1997) - "Modeling Low Reynolds Number Incompressible Flows Using SPH"

### 2. Granular Material Simulation

#### 2.1 Mohr-Coulomb Friction Model

$$|\mathbf{f}^{\text{friction}}| \leq \mu |\mathbf{f}^{\text{normal}}|$$

$$\mathbf{f}^{\text{friction}} = -\mu |\mathbf{f}^{\text{normal}}| \frac{\mathbf{v}^{\text{tangent}}}{|\mathbf{v}^{\text{tangent}}|}$$

Where:
- $\mu = \tan(\phi)$, $\phi$ = friction angle
- Used to simulate granular materials like sand

**Implementation**: `ComputeGranularForces()` in [SPHSolver.cs](Engine13/Utilities/SPHSolver.cs#L271-L333)

**Reference**: Daviet & Bertails-Descoubes (2016) - "A Semi-implicit Material Point Method for the Continuum Simulation of Granular Materials"

#### 2.2 Cohesion Force

$$\mathbf{f}^{\text{cohesion}} = -c \left(1 - \frac{r}{r_{\text{max}}}\right) \hat{\mathbf{r}}$$

- **Purpose**: Models inter-particle attraction (e.g., wet sand)
- **Implementation**: [SPHSolver.cs](Engine13/Utilities/SPHSolver.cs#L321-L328)

### 3. Molecular Dynamics & Spring Forces

#### 3.1 Hooke's Law with Damping

$$\mathbf{f} = -k(|\mathbf{r}| - L_0)\hat{\mathbf{r}} - c(\mathbf{v} \cdot \hat{\mathbf{r}})\hat{\mathbf{r}}$$

Where:
- $k$ = spring stiffness
- $L_0$ = rest length
- $c$ = damping coefficient
- $\mathbf{v}$ = relative velocity

**Implementation**: `PhysicsMath.HookeDampedForce()` in [MathHelpers.cs](Engine13/Utilities/MathHelpers.cs#L505-L509)

**Application**: `MolecularDynamicsSystem.Update()` in [MolecularDynamics.cs](Engine13/Utilities/MolecularDynamics.cs#L23-L44)

**Reference**: Hairer et al. (1993) - "Solving Ordinary Differential Equations I: Nonstiff Problems"

### 4. Time Integration

#### 4.1 Semi-Implicit Euler (Symplectic Euler)

$$\mathbf{v}_{n+1} = \mathbf{v}_n + \frac{\mathbf{f}_n}{m} \Delta t$$

$$\mathbf{x}_{n+1} = \mathbf{x}_n + \mathbf{v}_{n+1} \Delta t$$

- **Purpose**: More stable than explicit Euler for oscillatory systems
- **Implementation**: `Forces.Apply()` in [Forces.cs](Engine13/Utilities/Forces.cs#L30-L40)
- **Reference**: Verlet (1967) - "Computer Experiments on Classical Fluids"

### 5. Collision Detection & Resolution

#### 5.1 Separating Axis Theorem (SAT)

For convex polygons, two shapes are separated if there exists an axis where their projections don't overlap.

**Implementation**: `VertexCollisionSolver.TryFindContact()` in [MathHelpers.cs](Engine13/Utilities/MathHelpers.cs#L622-L862)

**Reference**: Gottschalk et al. (1996) - "OBBTree: A Hierarchical Structure for Rapid Interference Detection"

#### 5.2 Impulse-Based Collision Resolution

**Normal Impulse**:

$$j = \frac{-(1 + e) \mathbf{v}_{\text{rel}} \cdot \mathbf{n}}{m_A^{-1} + m_B^{-1}}$$

Where:
- $e$ = coefficient of restitution
- $\mathbf{v}_{\text{rel}}$ = relative velocity
- $\mathbf{n}$ = collision normal

**Implementation**: `PhysicsSolver.ResolveCollision()` in [MathHelpers.cs](Engine13/Utilities/MathHelpers.cs#L154-L448)

**Reference**: Catto (2005) - "Iterative Dynamics with Temporal Coherence" (Box2D physics engine)

#### 5.3 Coulomb Friction

**Static Friction**:
$$|\mathbf{f}_t| \leq \mu_s |\mathbf{f}_n|$$

**Kinetic Friction**:
$$\mathbf{f}_t = -\mu_k |\mathbf{f}_n| \hat{\mathbf{v}}_t$$

Where:
- $\mu_s$ = static friction coefficient
- $\mu_k$ = kinetic friction coefficient ($\approx 0.8 \mu_s$)

**Implementation**: [MathHelpers.cs](Engine13/Utilities/MathHelpers.cs#L398-L420)

**Reference**: Catto (2005)

#### 5.4 Baumgarte Stabilization

Positional correction to prevent drift:

$$\text{bias} = \beta \frac{\text{penetration}}{\Delta t}$$

Where $\beta \approx 0.08$ (Baumgarte scalar)

**Implementation**: [MathHelpers.cs](Engine13/Utilities/MathHelpers.cs#L335-L342)

**Reference**: Baumgarte (1972) - "Stabilization of constraints and integrals of motion in dynamical systems"

### 6. Spatial Partitioning

#### 6.1 Uniform Grid

Cell coordinates: 
$$\text{cell}(x, y) = \left(\left\lfloor \frac{x}{h} \right\rfloor, \left\lfloor \frac{y}{h} \right\rfloor\right)$$

- **Complexity**: O(n) insertion, O(1) neighbor query per particle
- **Implementation**: `SpatialGrid` in [MathHelpers.cs](Engine13/Utilities/MathHelpers.cs#L517-L597)

**Reference**: Ericson (2004) - "Real-Time Collision Detection"

### 7. Additional Physics

#### 7.1 Terminal Velocity

$$v_{\text{terminal}} = \sqrt{\frac{mg}{c_d}}$$

Where:
- $m$ = mass
- $g$ = gravitational acceleration
- $c_d$ = drag coefficient

**Implementation**: `MathHelpers.ComputeTerminalVelocityMag()` in [MathHelpers.cs](Engine13/Utilities/MathHelpers.cs#L879-L887)

#### 7.2 Angular Momentum (Rolling Friction)

Moment of inertia for circle: $I = \frac{1}{2}mr^2$

Angular impulse: $L = r \times \mathbf{f}$

**Implementation**: [MathHelpers.cs](Engine13/Utilities/MathHelpers.cs#L422-L447)

---

## Configuration & Presets

Particle materials are defined in `Engine13/Utilities/ParticlePresets.json`:

```json
{
  "Name": "Water",
  "Mass": 0.02,
  "SPHRestDensity": 1000.0,
  "SPHGasConstant": 2000.0,
  "SPHViscosity": 0.01,
  "ParticleRadius": 0.02,
  "IsFluid": true
}
```

**Key Parameters**:
- `SPHGasConstant`: Pressure stiffness (higher = less compressible)
- `SPHViscosity`: Internal friction (higher = more viscous)
- `Restitution`: Bounciness (0 = inelastic, 1 = elastic)
- `Friction`: Surface friction coefficient
- `BondStiffness`/`BondDamping`: Spring parameters for elastic materials

---

## Developer Notes

### Adding New Physics Features

1. **New Force Types**: Extend `Forces.cs` accumulator
2. **Custom Kernels**: Add to `SPHKernels` class
3. **Material Models**: Extend `SPHMaterialType` enum
4. **Integrators**: Modify `Forces.Apply()` method

### Performance Optimization

- Spatial grid cell size should be $\approx 2h$ (smoothing radius)
- Target 60 FPS with ~5000 particles on modern hardware
- Use `ThreadManager` for parallel force computation (future work)

### Debugging Tools

- **CSV Plotter**: Export simulation data to `Ticks.csv`
- **ImGui Windows**: Real-time parameter tuning
- **Debug Overlay**: Visualize forces, neighbors, pressure

---

## Known Issues

- [ ] **WCSPH Full Implementation**: Current pressure solver is simplified; full iterative pressure projection needed for incompressibility
- [ ] **Boundary Handling**: Ghost particles or boundary force methods not implemented
- [ ] **Surface Tension**: Cohesion forces are basic; proper surface tension (Akinci et al. 2013) pending
- [ ] **Two-Way Coupling**: Rigid body ↔ fluid interaction needs improvement
- [ ] **Nullable Warnings**: Code cleanup needed for C# nullable reference types

---

## Contributing

This is an educational project. Contributions are welcome via:
1. Open an issue describing the proposed change
2. Fork the repository
3. Submit a pull request with tests

**Priority Areas**:
- Implementing full WCSPH with pressure solve
- Adding IISPH (Implicit Incompressible SPH)
- GPU acceleration via compute shaders
- Unit tests for physics components

---

## References

### Primary Literature

1. **Müller, M., Charypar, D., & Gross, M.** (2003). *Particle-Based Fluid Simulation for Interactive Applications*. Proceedings of the 2003 ACM SIGGRAPH/Eurographics Symposium on Computer Animation, 154-159. [https://doi.org/10.2312/SCA03/154-159](https://doi.org/10.2312/SCA03/154-159)

2. **Monaghan, J. J.** (1992). *Smoothed Particle Hydrodynamics*. Annual Review of Astronomy and Astrophysics, 30(1), 543-574. [https://doi.org/10.1146/annurev.aa.30.090192.002551](https://doi.org/10.1146/annurev.aa.30.090192.002551)

3. **Becker, M., & Teschner, M.** (2007). *Weakly compressible SPH for free surface flows*. Proceedings of the 2007 ACM SIGGRAPH/Eurographics Symposium on Computer Animation, 209-217.

4. **Catto, E.** (2005). *Iterative Dynamics with Temporal Coherence*. Game Developer Conference. [https://box2d.org/publications/](https://box2d.org/publications/)

5. **Baumgarte, J.** (1972). *Stabilization of constraints and integrals of motion in dynamical systems*. Computer Methods in Applied Mechanics and Engineering, 1(1), 1-16.

6. **Daviet, G., & Bertails-Descoubes, F.** (2016). *A Semi-implicit Material Point Method for the Continuum Simulation of Granular Materials*. ACM Transactions on Graphics, 35(4), 1-13.

7. **Desbrun, M., & Gascuel, M. P.** (1996). *Smoothed Particles: A New Paradigm for Animating Highly Deformable Bodies*. Eurographics Workshop on Computer Animation and Simulation, 61-76.

8. **Morris, J. P., Fox, P. J., & Zhu, Y.** (1997). *Modeling Low Reynolds Number Incompressible Flows Using SPH*. Journal of Computational Physics, 136(1), 214-226.

9. **Ericson, C.** (2004). *Real-Time Collision Detection*. CRC Press. ISBN: 978-1558607323

10. **Hairer, E., Nørsett, S. P., & Wanner, G.** (1993). *Solving Ordinary Differential Equations I: Nonstiff Problems* (2nd ed.). Springer. ISBN: 978-3540566700

### Additional Resources

- **Box2D Physics Engine**: [https://box2d.org](https://box2d.org)
- **SPlisHSPlasH (SPH Library)**: [https://github.com/InteractiveComputerGraphics/SPlisHSPlasH](https://github.com/InteractiveComputerGraphics/SPlisHSPlasH)
- **Real-Time Fluid Dynamics for Games**: Jos Stam (2003)
- **Position Based Dynamics**: Müller et al. (2007)

---

## License

This project is an educational demonstration. No formal license currently applied. Contact the author for usage permissions.

**Developed by**: [Your Name]  
**Institution**: Year 13 School Project  
**Year**: 2025-2026

---

*For technical questions or collaboration inquiries, please open a GitHub issue.*
# Test commit
# Test commit
