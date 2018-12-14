using SixLabors.Primitives;

namespace Smartcrop
{
    public class Crop
    {
        public Crop(Rectangle area)
        {
            this.Area = area;
        }

        public Rectangle Area { get; internal set; }
        public Score Score { get; internal set; }
    }
}
