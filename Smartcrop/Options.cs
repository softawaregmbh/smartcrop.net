namespace Smartcrop
{
    public class Options
    {
        public Options(int width, int height)
        {
            this.Width = width;
            this.Height = height;
        }

        public int Width = 0;
        public int Height = 0;
        public int Aspect = 0;
        public float DetailWeight = 0.2f;
        public (float red, float green, float blue) SkinColor = (0.78f, 0.57f, 0.44f);
        public float SkinBias = 0.01f;
        public float SkinBrightnessMin = 0.2f;
        public float SkinBrightnessMax = 1.0f;
        public float SkinThreshold = 0.8f;
        public float SkinWeight = 1.8f;
        public float SaturationBrightnessMin = 0.05f;
        public float SaturationBrightnessMax = 0.9f;
        public float SaturationThreshold = 0.4f;
        public float SaturationBias = 0.2f;
        public float SaturationWeight = 0.1f;
        // Step * minscale rounded down to the next power of two should be good
        public int ScoreDownSample = 8;
        public int Step = 8;
        public float ScaleStep = 0.1f;
        public float MinScale = 1.0f;
        public float MaxScale = 1.0f;
        public float EdgeRadius = 0.4f;
        public float edgeWeight = -20.0f;
        public float OutsideImportance = -0.5f;
        public float BoostWeight = 100.0f;
        public bool RuleOfThirds = true;
        public bool Prescale = true;
        public bool Debug = false;

        internal int? CropWidth = 0;
        internal int? CropHeight = 0;
    }
}
