using System;

namespace Engine13.Utilities
{
    /// <summary>
    /// Global physics settings that can be adjusted at runtime through the UI.
    /// These settings affect all physics calculations in the simulation.
    /// </summary>
    public static class PhysicsSettings
    {
        private static float _gravitationalConstant = 9.81f;
        private static float _airResistance = 0.0f;

        /// <summary>
        /// Gravitational acceleration constant in m/s².
        /// Default is 9.81 (Earth's gravity).
        /// Range: 0.0 (zero gravity) to 50.0 (extreme gravity)
        /// </summary>
        public static float GravitationalConstant
        {
            get => _gravitationalConstant;
            set => _gravitationalConstant = Math.Clamp(value, 0f, 50f);
        }

        /// <summary>
        /// Air resistance coefficient applied to all particles.
        /// 0.0 = no resistance (vacuum), 1.0 = maximum resistance
        /// This simulates drag force proportional to velocity.
        /// </summary>
        public static float AirResistance
        {
            get => _airResistance;
            set => _airResistance = Math.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// Reset all physics settings to their default values.
        /// </summary>
        public static void Reset()
        {
            _gravitationalConstant = 9.81f;
            _airResistance = 0.0f;
        }
    }
}
