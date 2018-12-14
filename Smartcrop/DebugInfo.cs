using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Smartcrop
{
    public class DebugInfo
    {
        public Image<Rgba32> Output { get; internal set; }
        public Options Options { get; internal set; }
        public IReadOnlyList<Crop> Crops { get; internal set; }
    }
}
