using System.Collections.Generic;
using SkiaSharp;

namespace Smartcrop
{
    public class DebugInfo
    {
        public SKBitmap Output { get; internal set; }
        public Options Options { get; internal set; }
        public IReadOnlyList<Crop> Crops { get; internal set; }
    }
}
