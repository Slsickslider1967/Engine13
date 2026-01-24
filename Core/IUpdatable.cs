namespace Engine13.Core
{
    /// <summary>
    /// Contract for objects that require per-frame updates.
    /// Implement this interface for any component or system that needs to be updated each frame.
    /// </summary>
    public interface IUpdatable
    {
        /// <summary>Called once per frame to update the object's state.</summary>
        /// <param name="gameTime">Provides timing information for frame-independent updates.</param>
        void Update(GameTime gameTime);
    }
}
