using System.Runtime.CompilerServices;

namespace Smartcrop
{
    public struct Rectangle
    {
        public Rectangle(int x, int y, int width, int height)
        {
            this.X = x;
            this.Y = y;
            this.Width = width;
            this.Height = height;
        }

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public int Left => this.X;
        public int Top => this.Y;

        public int Right
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => unchecked(this.X + this.Width);
        }

        public int Bottom
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => unchecked(this.Y + this.Height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Rectangle rectangle) =>
            (this.X <= rectangle.X) && (rectangle.Right <= this.Right) &&
            (this.Y <= rectangle.Y) && (rectangle.Bottom <= this.Bottom);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IntersectsWith(Rectangle rectangle) =>
            (rectangle.X < this.Right) && (this.X < rectangle.Right) &&
            (rectangle.Y < this.Bottom) && (this.Y < rectangle.Bottom);
    }
}
