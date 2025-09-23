namespace Engine13.Core
{
    public abstract class Scene
    {
        public abstract void Update(GameTime gameTime); // Update method with GameTime parameter
        public abstract void Draw(); // Draw method - to be implemented by derived classes
    }
}