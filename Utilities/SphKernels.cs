using System.Numerics;

namespace Engine13.Utilities
{
    public static class SphKernels
    {
        // Poly6 kernel (2D) - normalized coefficient depends on h
        public static float Poly6(float r, float h)
        {
            if (r < 0f) r = 0f;
            if (r >= h) return 0f;
            float hr2 = h * h - r * r;
            float coeff = 4f / (MathF.PI * MathF.Pow(h, 8));
            return coeff * hr2 * hr2 * hr2;
        }

        // Spiky gradient kernel (2D) - returns gradient vector
        public static Vector2 SpikyGradient(float r, float h, Vector2 dir)
        {
            if (r <= 0f || r >= h) 
                return Vector2.Zero;
            float x = h - r;
            float coeff = -30f / (MathF.PI * MathF.Pow(h, 5));
            return coeff * x * x * dir;
        }

        // Viscosity laplacian kernel (2D)
        public static float ViscosityLaplacian(float r, float h)
        {
            if (r < 0f) r = 0f;
            if (r >= h)
                return 0f;
            float coeff = 40f / (MathF.PI * MathF.Pow(h, 5));
            return coeff * (h - r);
        }
    }
}
