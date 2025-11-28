# Element Properties System

## Overview
A comprehensive element properties system has been implemented to accurately model materials in molecular dynamics simulations. This system separates atomic/molecular properties from simulation behavior.

## Architecture

### 1. Element Class (`Utilities/jsonReader.cs`)
Contains physical properties of individual elements:
- **Name** - Element name (e.g., "Hydrogen")
- **AtomicMass** - Atomic mass in amu
- **AtomicRadius** - Atomic radius in meters
- **Electronegativity** - Pauling electronegativity value
- **LJ_Epsilon** - Lennard-Jones epsilon parameter (kJ/mol)
- **LJ_Sigma** - Lennard-Jones sigma parameter (meters)
- **IonizationEnergy** - First ionization energy (kJ/mol)
- **MeltingPoint** - Melting point (Kelvin)
- **BoilingPoint** - Boiling point (Kelvin)

### 2. Elements.json
Contains scientifically accurate data for 15 common elements:
- **Gases**: H, He, N, O, Ne, Ar
- **Metals**: Na, Al, Fe, Cu, Ag, Au
- **Nonmetals**: C, Si, Cl

Each element entry includes all physical properties needed for accurate MD simulation.

### 3. MDPresetReader Enhancements
The material preset system now supports:
- **ElementComposition** - Array of element symbols (e.g., `["H", "H", "O"]` for water)
- **GetElements()** - Retrieves Element objects for composition
- **GetMolecularMass()** - Calculates mass from constituent elements
- **GetAverageLJParameters()** - Computes average LJ parameters from elements

### 4. Material Presets (MDPresets.json)
Updated presets now include element composition:
- **Water**: ["H", "H", "O"] → H₂O
- **Steel**: ["Fe"] → Iron-based alloy
- **Rubber**: ["C", "H", "H"] → Hydrocarbon polymer
- **Wood**: ["C", "C", "H", "O"] → Cellulose-like composition

## Usage

### Loading Elements
```csharp
// Load a single element
var hydrogen = Element.Load("H");
Console.WriteLine($"{hydrogen.Name}: {hydrogen.AtomicMass} amu");

// Get all elements
var allElements = Element.GetAllElements();
```

### Using in Materials
```csharp
// Load a material preset
var waterPreset = MDPresetReader.Load("Water");

// Get element composition
var elements = waterPreset.GetElements();
// Returns: [Element(H), Element(H), Element(O)]

// Calculate molecular properties
float molecularMass = waterPreset.GetMolecularMass();
// Returns: 18.015 amu (1.008 + 1.008 + 15.999)

var (epsilon, sigma) = waterPreset.GetAverageLJParameters();
// Returns averaged LJ parameters from constituent elements
```

### In Game.cs
```csharp
var preset = MDPresetReader.Load("Water");
var elements = preset.GetElements();
float molecularMass = preset.GetMolecularMass();

// Use molecular mass for particles
particle.Mass = molecularMass;

// Log composition info
Console.WriteLine($"Composition: {string.Join(", ", elements.Select(e => e.Symbol))}");
Console.WriteLine($"Molecular Mass: {molecularMass:F3} amu");
```

## Benefits

### 1. Scientific Accuracy
- Real atomic/molecular data from chemistry databases
- Proper mass calculations based on composition
- Realistic intermolecular force parameters

### 2. Modularity
- Elements defined once, reused in multiple materials
- Easy to add new elements to the periodic table
- Material properties calculated from constituent elements

### 3. Scalability
- Start with 15 common elements
- Expand to full periodic table as needed
- Support for complex molecules and compounds

### 4. Flexibility
- Override calculated properties with preset values when needed
- Mix element-derived and custom simulation parameters
- Support for both pure elements and compounds

## Adding New Elements

To add a new element to the system:

1. Add entry to `Elements.json`:
```json
"Ca": {
  "name": "Calcium",
  "atomicMass": 40.078,
  "atomicRadius": 1.97e-10,
  "electronegativity": 1.00,
  "lj_epsilon": 0.0290,
  "lj_sigma": 2.58e-10,
  "ionizationEnergy": 589.8,
  "meltingPoint": 1115.0,
  "boilingPoint": 1757.0
}
```

2. Use in material presets:
```json
"Bone": {
  "Name": "Bone",
  "Elements": ["Ca", "P", "O"],
  ...
}
```

## Data Sources
Element properties sourced from:
- NIST Chemistry WebBook
- CRC Handbook of Chemistry and Physics
- Lennard-Jones parameters from molecular dynamics literature

## Future Enhancements
- Support for ionic bonds and charges
- Temperature-dependent phase transitions
- Bond angle constraints for molecular geometry
- Coulombic interactions for polar molecules
- Reaction mechanisms and bond formation/breaking
