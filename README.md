# Engine13
Year 13 project 

## Molecular dynamics attribute

Engine13 now drives particle simulations through the `MolecularDynamics` entity attribute found in `Utilities/Attritibute.cs`. Each spawned particle in `Game.CreateObjects` registers the component, enabling:

- spring-like bonding between nearby particles (configurable rest length, stiffness, and bond limits),
- Lennard-Jones style non-bonded interactions for short-range attraction/repulsion,
- optional local oscillation/drive behaviour for anchored particles.

Tweak the parameters inside `CreateObjects` (or directly on the component) to explore different materials, e.g. adjust `BondSpringConstant` for stiffer lattices, `LJ_Epsilon` for stickiness, or `MaxBondsPerEntity` for branching structures. Call `MolecularDynamics.ClearAllBonds()` when resetting the scene to drop any persistent bonds.
