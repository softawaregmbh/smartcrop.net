using System.Collections.Generic;
using SkiaSharp;

namespace Smartcrop
{
    public class DebugInfo
    {
        public byte[] Output { get; internal set; }
        public Options Options { get; internal set; }
        public IReadOnlyList<Crop> Crops { get; internal set; }
    }
}
