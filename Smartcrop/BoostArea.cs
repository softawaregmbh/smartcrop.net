namespace Smartcrop
{
    public class BoostArea
    {
        public BoostArea(Rectangle area, float weight)
        {
            this.Area = area;
            this.Weight = weight;
        }

        public Rectangle Area { get; set; }
        public float Weight { get; set; }
    }
}
