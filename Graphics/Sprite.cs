using System.Numerics;
using Engine13.Core;
using Veldrid;

namespace Engine13.Graphics
{
    public class Sprite : IUpdatable
    {
        private Entity _entity;
        private Texture? _texture;
        private Vector2 _textureOffset = Vector2.Zero;
        private Vector2 _textureScale = Vector2.One;

        // Transform properties
        public Vector2 Position
        {
            get => _entity.Position;
            set => _entity.Position = value;
        }

        public Vector2 Scale { get; set; } = Vector2.One;
        public float Rotation { get; set; } = 0f;

        // Visual properties
        public Vector4 Tint
        {
            get => _entity.Colour;
            set => _entity.Colour = value;
        }

        public float Alpha
        {
            get => _entity.Colour.W;
            set =>
                _entity.Colour = new Vector4(
                    _entity.Colour.X,
                    _entity.Colour.Y,
                    _entity.Colour.Z,
                    value
                );
        }

        // Animation checks
        public SpriteAnimation? CurrentAnimation { get; private set; }
        public bool IsAnimating => CurrentAnimation != null && CurrentAnimation.IsPlaying;

        // Rendering properties
        public int Layer { get; set; } = 0;
        public BlendMode BlendMode { get; set; } = BlendMode.Alpha;

        public Sprite(GraphicsDevice gd, float width = 1f, float height = 1f)
        {
            _entity = Engine13.Primitives.QuadFactory.CreateQuad(gd, width, height);
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

        public void SetTextureRegion(
            int x,
            int y,
            int width,
            int height,
            int textureWidth,
            int textureHeight
        )
        {
            _textureOffset = new Vector2((float)x / textureWidth, (float)y / textureHeight);
            _textureScale = new Vector2((float)width / textureWidth, (float)height / textureHeight);
        }

        //Animations duhhh
        public void PlayAnimation(SpriteAnimation animation, bool loop = true)
        {
            CurrentAnimation = animation;
            CurrentAnimation.Play(loop);
        }

        public void StopAnimation()
        {
            if (CurrentAnimation != null)
            {
                CurrentAnimation.Stop();
            }
        }

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

        public Entity GetEntity() => _entity; //Entity for textures

        public Texture? GetTexture() => _texture;

        public Vector2 GetTextureOffset() => _textureOffset;

        public Vector2 GetTextureScale() => _textureScale;
    }

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

        public AnimationFrame(
            int x,
            int y,
            int width,
            int height,
            int textureWidth,
            int textureHeight,
            float duration = 0.1f
        )
        {
            TextureOffset = new Vector2((float)x / textureWidth, (float)y / textureHeight);
            TextureScale = new Vector2((float)width / textureWidth, (float)height / textureHeight);
            Duration = duration;
        }
    }

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
            if (frames == null)
                throw new System.ArgumentNullException(nameof(frames));

            _frames = frames;
            if (_frames.Length == 0)
                throw new System.ArgumentException(
                    "Animation must have at least one frame",
                    nameof(frames)
                );
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
            if (!IsPlaying || _frames.Length == 0)
                return;

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
            if (_frames.Length == 0)
                return null;
            return _frames[_currentFrameIndex];
        }

        public int GetFrameCount() => _frames.Length;

        public int GetCurrentFrameIndex() => _currentFrameIndex;
    }

    public enum BlendMode
    {
        None, // No blending
        Alpha, // Standard alpha blending
        Additive, // Additive blending
        Multiply, // Multiplicative blending
        Screen, // Screen blending
    }

    public static class SpriteFactory
    {
        /// <summary>
        /// Creates a basic colored sprite (no texture)
        /// </summary>
        public static Sprite CreateColoredSprite(
            GraphicsDevice gd,
            float width,
            float height,
            Vector4 color
        )
        {
            var sprite = new Sprite(gd, width, height);
            sprite.Tint = color;
            return sprite;
        }

        public static Sprite CreateTexturedSprite(
            GraphicsDevice gd,
            Texture texture,
            float width,
            float height
        )
        {
            return new Sprite(gd, texture, width, height);
        }

        public static Sprite CreateSpriteFromTexture(GraphicsDevice gd, Texture texture)
        {
            // TODO: Get actual texture dimensions when Texture class is implemented
            return new Sprite(gd, texture, 1f, 1f);
        }
    }
}
