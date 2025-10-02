namespace Engine13.Core
{
    // Generic contract for anything that needs per-frame updates
    public interface IUpdatable
    {
        void Update(GameTime gameTime);
    }
}
