using Veldrid;
using System.Numerics;
using Engine13.Core;

namespace Engine13.Graphics
{
    public class Sprite : IUpdatable
    {
        private Mesh _mesh;
        private Texture? _texture;
        private Vector2 _textureOffset = Vector2.Zero;
        private Vector2 _textureScale = Vector2.One;
        
        // Transform properties
        public Vector2 Position 
        { 
            get => _mesh.Position; 
            set => _mesh.Position = value; 
        }
        
        public Vector2 Scale { get; set; } = Vector2.One;
        public float Rotation { get; set; } = 0f;
        
        // Visual properties
        public Vector4 Tint 
        { 
            get => _mesh.Color; 
            set => _mesh.Color = value; 
        }
        
        public float Alpha 
        { 
            get => _mesh.Color.W; 
            set => _mesh.Color = new Vector4(_mesh.Color.X, _mesh.Color.Y, _mesh.Color.Z, value); 
        }

        // Animation checks
        public SpriteAnimation? CurrentAnimation { get; private set; }
        public bool IsAnimating => CurrentAnimation != null && CurrentAnimation.IsPlaying;
        
        // Rendering properties
        public int Layer { get; set; } = 0;
        public BlendMode BlendMode { get; set; } = BlendMode.Alpha;

        public Sprite(GraphicsDevice gd, float width = 1f, float height = 1f)
        {
            _mesh = Engine13.Primitives.QuadFactory.CreateQuad(gd, width, height);
        }

        public Sprite(GraphicsDevice gd, Texture texture, float width = 1f, float height = 1f) 
            : this(gd, width, height)
        {
            _texture = texture;
        }
        public void SetTexture(Texture texture)
        {
            _texture = texture;
        }
        public void SetTextureRegion(Vector2 offset, Vector2 scale)
        {
            _textureOffset = offset;
            _textureScale = scale;
            // Update UV coordinates on the mesh when texture system is implemented
        }

        /// <summary>
        /// Sets the texture region by pixel coordinates (useful for sprite sheets)
        /// </summary>
        public void SetTextureRegion(int x, int y, int width, int height, int textureWidth, int textureHeight)
        {
            _textureOffset = new Vector2((float)x / textureWidth, (float)y / textureHeight);
            _textureScale = new Vector2((float)width / textureWidth, (float)height / textureHeight);
        }

        /// <summary>
        /// Plays an animation on this sprite
        /// </summary>
        public void PlayAnimation(SpriteAnimation animation, bool loop = true)
        {
            CurrentAnimation = animation;
            CurrentAnimation.Play(loop);
        }

        /// <summary>
        /// Stops the current animation
        /// </summary>
        public void StopAnimation()
        {
            CurrentAnimation?.Stop();
        }

        /// <summary>
        /// Updates the sprite (handles animations)
        /// </summary>
        public void Update(GameTime gameTime)
        {
            if (CurrentAnimation != null && CurrentAnimation.IsPlaying)
            {
                CurrentAnimation.Update(gameTime);
                
                // Update texture region based on current animation frame
                var frame = CurrentAnimation.GetCurrentFrame();
                if (frame != null)
                {
                    SetTextureRegion(frame.TextureOffset, frame.TextureScale);
                }
            }
        }

        public Mesh GetMesh() => _mesh; //Mesh for tetures
        public Texture? GetTexture() => _texture; 
        public Vector2 GetTextureOffset() => _textureOffset; //Allining texture with messh 

        /// <summary>
        /// Gets the current texture scale for UV mapping
        /// </summary>
        public Vector2 GetTextureScale() => _textureScale;
    }

    /// <summary>
    /// Represents an animation frame with texture coordinates
    /// </summary>
    public class AnimationFrame
    {
        public Vector2 TextureOffset { get; set; }
        public Vector2 TextureScale { get; set; }
        public float Duration { get; set; } // Duration in seconds

        public AnimationFrame(Vector2 offset, Vector2 scale, float duration = 0.1f)
        {
            TextureOffset = offset;
            TextureScale = scale;
            Duration = duration;
        }

        public AnimationFrame(int x, int y, int width, int height, int textureWidth, int textureHeight, float duration = 0.1f)
        {
            TextureOffset = new Vector2((float)x / textureWidth, (float)y / textureHeight);
            TextureScale = new Vector2((float)width / textureWidth, (float)height / textureHeight);
            Duration = duration;
        }
    }

    /// <summary>
    /// Manages sprite animation playback
    /// </summary>
    public class SpriteAnimation
    {
        private readonly AnimationFrame[] _frames;
        private int _currentFrameIndex = 0;
        private float _timeInCurrentFrame = 0f;
        
        public bool IsPlaying { get; private set; } = false;
        public bool IsLooping { get; private set; } = false;
        public string Name { get; }

        public SpriteAnimation(string name, params AnimationFrame[] frames)
        {
            Name = name;
            _frames = frames ?? throw new System.ArgumentNullException(nameof(frames));
            if (_frames.Length == 0)
                throw new System.ArgumentException("Animation must have at least one frame", nameof(frames));
        }

        public void Play(bool loop = true)
        {
            IsPlaying = true;
            IsLooping = loop;
            _currentFrameIndex = 0;
            _timeInCurrentFrame = 0f;
        }

        public void Stop()
        {
            IsPlaying = false;
            _currentFrameIndex = 0;
            _timeInCurrentFrame = 0f;
        }

        public void Pause()
        {
            IsPlaying = false;
        }

        public void Resume()
        {
            IsPlaying = true;
        }

        public void Update(GameTime gameTime)
        {
            if (!IsPlaying || _frames.Length == 0) return;

            _timeInCurrentFrame += gameTime.DeltaTime;

            var currentFrame = _frames[_currentFrameIndex];
            if (_timeInCurrentFrame >= currentFrame.Duration)
            {
                _timeInCurrentFrame = 0f;
                _currentFrameIndex++;

                if (_currentFrameIndex >= _frames.Length)
                {
                    if (IsLooping)
                    {
                        _currentFrameIndex = 0;
                    }
                    else
                    {
                        _currentFrameIndex = _frames.Length - 1;
                        IsPlaying = false;
                    }
                }
            }
        }

        public AnimationFrame? GetCurrentFrame()
        {
            if (_frames.Length == 0) return null;
            return _frames[_currentFrameIndex];
        }

        public int GetFrameCount() => _frames.Length;
        public int GetCurrentFrameIndex() => _currentFrameIndex;
    }

    /// <summary>
    /// Blend modes for sprite rendering
    /// </summary>
    public enum BlendMode
    {
        None,        // No blending
        Alpha,       // Standard alpha blending
        Additive,    // Additive blending
        Multiply,    // Multiplicative blending
        Screen       // Screen blending
    }

    /// <summary>
    /// Factory for creating common sprite types
    /// </summary>
    public static class SpriteFactory
    {
        /// <summary>
        /// Creates a basic colored sprite (no texture)
        /// </summary>
        public static Sprite CreateColoredSprite(GraphicsDevice gd, float width, float height, Vector4 color)
        {
            var sprite = new Sprite(gd, width, height);
            sprite.Tint = color;
            return sprite;
        }

        /// <summary>
        /// Creates a textured sprite
        /// </summary>
        public static Sprite CreateTexturedSprite(GraphicsDevice gd, Texture texture, float width, float height)
        {
            return new Sprite(gd, texture, width, height);
        }

        /// <summary>
        /// Creates a sprite sized to match its texture (when texture system is implemented)
        /// </summary>
        public static Sprite CreateSpriteFromTexture(GraphicsDevice gd, Texture texture)
        {
            // TODO: Get actual texture dimensions when Texture class is implemented
            return new Sprite(gd, texture, 1f, 1f);
        }
    }
}