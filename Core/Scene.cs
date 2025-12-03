namespace Engine13.Core
{
    /// <summary>
    /// Base class for game scenes. Scenes represent distinct game states or levels.
    /// Each scene manages its own entities, updates, and rendering.
    /// </summary>
    public abstract class Scene
    {
        /// <summary>Called once per frame to update scene logic.</summary>
        /// <param name="gameTime">Provides timing information for frame-independent updates.</param>
        public abstract void Update(GameTime gameTime);

        /// <summary>Called to render the scene's visual elements.</summary>
        public abstract void Draw();
    }
}
