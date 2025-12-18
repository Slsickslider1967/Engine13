# Engine-13

Engine-13 is a lightweight experimental 2D/3D particle engine and simulation playground developed as a Year 13 school project. It provides a small rendering + UI layer with ImGui, a particle system, basic collision handling, and molecular-dynamics style bond forces. The project is intended for exploration, learning, and iterative development of physics systems (SPH/WCSPH, springs, collisions, etc.).

**Quick Links**
- **Source:** `./Engine-13` (project files and main executable project)
- **Core code:** `./Core`, `./Graphics`, `./Utilities`, `./Input`, `./UI`

**Supported platforms**: Windows, Linux, macOS (primary development on Linux). Builds target `.NET 9`/`.NET 10` TFMs present in the project files.

**Status:** Prototype / experimental — many systems are educational and evolving. See "Known Issues" below.

**License:** (No license file included) — treat repository as private unless you add a license.

**Table of contents**
- **Build & Run**
- **Features**
- **Project structure**
- **Physics & Simulation**
- **Configuration & Presets**
- **Developer notes**
- **Known issues**
- **Contributing**

**Build & Run**

- **Prerequisites:** Install the .NET SDK (9 or 10 as available). On Linux, use the distro package manager or download from microsoft.com.
- **Build the solution (from repo root):**

```bash
dotnet build Engine-13.sln -c Debug
```

- **Run the main project:**

```bash
dotnet run --project Engine-13/Engine-13.csproj -c Debug
```

- After a successful build, the executable is available in `Engine-13/bin/Debug/net10.0/` (or `net9.0` depending on SDK). You can run that binary directly on the matching runtime.

**Features**

- **Particle system:** create and spawn particle clouds, with configurable presets for mass, size, and SPH-like parameters.
- **Molecular dynamics (bonds):** spring-like bonds between nearby particles with configurable rest lengths, stiffness, and damping.
- **Collision handling:** impulse-based collision resolver and edge/wall collision handling.
- **ImGui-based debug UI:** windows for simulation control, CSV plotting, and on-the-fly parameter tuning.
- **CSV plotter:** simple CSV-to-plot window with Y-axis ticks and labels for quick diagnostics.

**Project structure (important folders)**

- **`Core/`**: game loop, `Game.cs`, entity registration and simulation orchestration.
- **`Engine-13/`**: main executable project files, shaders, and runtime assets.
- **`Graphics/`**: rendering layer, `Renderer.cs`, `Mesh.cs`, `Texture.cs`.
- **`Utilities/`**: physics helpers, `Forces.cs`, `MathHelpers.cs`, `ParticlePresets.json`, and the molecular dynamics implementation.
- **`UI/`**: `ImGuiController.cs` and ImGui-driven windows.

**Physics & Simulation (overview)**

- **Forces integration:** A central `Forces` accumulator collects forces and applies them using a semi-implicit approach for stability.
- **Particle dynamics:** Particles are simulated with per-frame integration, optional SPH-like pressure/viscosity behavior (WCSPH not yet implemented). The `ParticleSystem` and `ParticleDynamics` types are responsible for neighbor queries and per-particle updates.
- **Molecular dynamics (bonds):** Implemented in `Utilities/MolecularDynamics.cs` (singleton `MolecularDynamicsSystem`) — it manages `Bond` objects (rest length, stiffness, damping) and applies spring + damping forces between bonded particles.
- **Collision resolution:** `Utilities/MathHelpers.cs` contains the impulse-based `PhysicsSolver.ResolveCollision` and positional corrections.

**Configuration & Presets**

- Particle presets are stored in `Utilities/ParticlePresets.json`. Presets include physical properties such as `Mass`, `RestDensity`, `SmoothingRadius`, and bond-related parameters like `BondStiffness` and `BondDamping`.
- Edit presets or add new ones to experiment with different materials (dense solids, fluids, granular media).

**Developer notes**

- To add a new particle preset, edit `Utilities/ParticlePresets.json`. The JSON loader is in `Utilities/jsonReader.cs`.
- The main simulation UI is in `Core/Game.cs` — it contains spawn controls and the selection/fill tools used to create particle groups.
- When adding new systems that reference core types, remember to include the correct namespaces (e.g., `using Engine13.Graphics;`) to avoid compile errors.

**Known issues & TODOs**

- WCSPH pressure solver is not implemented yet — currently the project uses an ad-hoc pressure/viscosity approach in `ParticleDynamics`.
- Several nullable-reference warnings exist across the project; they are non-fatal but should be cleaned up before wider distribution.
- UI controls and keyboard/mouse mappings are minimal and may change as the project evolves.

**Quick developer checklist**

- Clone the repo.
- Install .NET SDK 9/10.
- Build solution: `dotnet build Engine-13.sln -c Debug`.
- Run locally: `dotnet run --project Engine-13/Engine-13.csproj -c Debug`.

**Contribution**

- This repository is a personal/educational project. If you plan to contribute, please open an issue describing the change first so we can coordinate.

**Contact / Notes**

- The codebase is under active development. If you want me to add more developer documentation (WCSPH design doc, API reference for `ParticleSystem`, unit tests, or a fast-start scene), tell me which item and I'll prepare it next.
