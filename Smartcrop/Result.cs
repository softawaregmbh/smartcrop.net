using SixLabors.Primitives;

namespace Smartcrop
{
    public class Result
    {
        public Rectangle Area { get; internal set; }
        public DebugInfo DebugInfo { get; internal set; }
    }
}
