using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;

namespace Smartcrop
{
    public class ImageCrop
    {
        private Options options;

        public ImageCrop(int width, int height)
            : this(new Options(width, height))
        {
        }

        public ImageCrop(Options options)
        {
            this.Options = options;
        }

        public Options Options
        {
            get => this.options;
            set => this.options = value ?? throw new ArgumentNullException(nameof(value));
        }

        public Result Crop(Image<Rgba32> image, params BoostArea[] boostAreas)
        {
            Image<Rgba32> clone = null;

            try
            {
                if (this.Options.Aspect > 0)
                {
                    this.Options.Width = this.Options.Aspect;
                    this.Options.Height = 1;
                }

                var scale = 1f;
                var prescale = 1f;

                if (this.Options.Width > 0 && this.Options.Height > 0)
                {
                    scale = Math.Min(
                        image.Width / (float)this.Options.Width,
                        image.Height / (float)this.Options.Height
                    );
                    this.Options.CropWidth = (int)Math.Round(this.Options.Width * scale);
                    this.Options.CropHeight = (int)Math.Round(this.Options.Height * scale);

                    // Img = 100x100; width = 95x95; scale = 100/95; 1/scale > min
                    // don't set minScale smaller than 1/scale
                    // -> don't pick crops that need upscaling
                    this.Options.MinScale = Math.Min(
                        this.Options.MaxScale,
                        Math.Max(1.0f / scale, this.Options.MinScale)
                    );

                    // prescale if possible
                    if (this.Options.Prescale)
                    {
                        prescale = Math.Min(Math.Max(256f / image.Width, 256f / image.Height), 1);
                        if (prescale < 1)
                        {
                            image = clone = image.Clone();
                            image.Mutate(o => o.Resize((int)Math.Round(image.Width * prescale), (int)Math.Round(image.Height * prescale)));

                            this.Options.CropWidth = (int)Math.Round(this.Options.CropWidth.Value * (double)prescale);
                            this.Options.CropHeight = (int)Math.Round(this.Options.CropHeight.Value * (double)prescale);

                            for (int i = 0; i < boostAreas.Length; i++)
                            {
                                var area = boostAreas[i].Area;
                                boostAreas[i].Area =
                                    new Rectangle(
                                        area.X = (int)Math.Round(area.X * prescale),
                                        area.Y = (int)Math.Round(area.Y * prescale),
                                        area.Width = (int)Math.Round(area.Width * prescale),
                                        area.Height = (int)Math.Round(area.Height * prescale));
                            }
                        }
                        else
                        {
                            prescale = 1;
                        }
                    }
                }

                var result = this.Analyze(image, boostAreas);

                if (this.Options.Prescale)
                {
                    result.Area = new Rectangle(
                    (int)Math.Round(result.Area.X / prescale),
                    (int)Math.Round(result.Area.Y / prescale),
                    (int)Math.Round(result.Area.Width / prescale),
                    (int)Math.Round(result.Area.Height / prescale));

                    if (this.Options.Debug)
                    {
                        foreach (var crop in result.DebugInfo.Crops)
                        {
                            crop.Area = new Rectangle(
                                (int)Math.Round(crop.Area.X / prescale),
                                (int)Math.Round(crop.Area.Y / prescale),
                                (int)Math.Round(crop.Area.Width / prescale),
                                (int)Math.Round(crop.Area.Height / prescale));
                        }
                    }
                }

                return result;
            }
            finally
            {
                clone?.Dispose();
            }
        }

        private Result Analyze(Image<Rgba32> input, params BoostArea[] boostAreas)
        {
            var output = new Image<Rgba32>(input.Width, input.Height);

            try
            {
                var result = new Result();

                this.EdgeDetect(input, output);
                this.SkinDetect(input, output);
                this.SaturationDetect(input, output);
                this.ApplyBoosts(output, boostAreas);

                var scoreOutput = this.DownSample(output);

                var topScore = double.MinValue;
                var crops = this.GenerateCrops(input.Width, input.Height);

                foreach (var crop in crops)
                {
                    crop.Score = this.Score(scoreOutput, crop.Area, boostAreas);
                    if (crop.Score.Total > topScore)
                    {
                        result.Area = crop.Area;
                        topScore = crop.Score.Total;
                    }
                }

                if (this.Options.Debug)
                {
                    result.DebugInfo = new DebugInfo()
                    {
                        Crops = crops,
                        Output = output,
                        Options = this.Options
                    };

                    // don't dispose output in this case
                    output = null;
                }

                return result;
            }
            finally
            {
                output?.Dispose();
            }
        }

        private void EdgeDetect(Image<Rgba32> input, Image<Rgba32> output)
        {
            var w = input.Width;
            var h = input.Height;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    float lightness;

                    if (x == 0 || x >= w - 1 || y == 0 || y >= h - 1)
                    {
                        lightness = this.Sample(input, x, y);
                    }
                    else
                    {
                        lightness =
                            this.Sample(input, x, y) * 4 -
                            this.Sample(input, x, y - 1) -
                            this.Sample(input, x - 1, y) -
                            this.Sample(input, x + 1, y) -
                            this.Sample(input, x, y + 1);
                    }

                    var pixel = output[x, y];
                    pixel.G = (byte)Math.Min(byte.MaxValue, Math.Max(0, Math.Round(lightness)));
                    output[x, y] = pixel;
                }
            }
        }

        private void SkinDetect(Image<Rgba32> input, Image<Rgba32> output)
        {
            float SkinColor(Rgba32 pixel)
            {
                var mag = (float)Math.Sqrt(pixel.R * pixel.R + pixel.G * pixel.G + pixel.B * pixel.B);
                var rd = pixel.R / mag - this.Options.SkinColor.R;
                var gd = pixel.G / mag - this.Options.SkinColor.G;
                var bd = pixel.B / mag - this.Options.SkinColor.B;
                var d = (float)Math.Sqrt(rd * rd + gd * gd + bd * bd);
                return 1f - d;
            }

            for (var y = 0; y < input.Height; y++)
            {
                for (var x = 0; x < input.Width; x++)
                {
                    var pixel = input[x, y];
                    var lightness = this.Cie(input[x, y]) / 255f;
                    var skin = SkinColor(pixel);
                    var isSkinColor = skin > this.Options.SkinThreshold;
                    var isSkinBrightness =
                        lightness >= this.Options.SkinBrightnessMin &&
                        lightness <= this.Options.SkinBrightnessMax;

                    pixel = output[x, y];
                    if (isSkinColor && isSkinBrightness)
                    {
                        pixel.R = (byte)Math.Min(byte.MaxValue, (skin - this.Options.SkinThreshold) * (255f / (1f - this.Options.SkinThreshold)));
                    }
                    else
                    {
                        pixel.R = 0;
                    }

                    output[x, y] = pixel;
                }
            }
        }

        private void SaturationDetect(Image<Rgba32> input, Image<Rgba32> output)
        {
            float Saturation(Rgba32 pixel)
            {
                var maximum = Math.Max(pixel.R / 255f, Math.Max(pixel.G / 255f, pixel.B / 255f));
                var minumum = Math.Min(pixel.R / 255f, Math.Max(pixel.G / 255f, pixel.B / 255f));

                if (maximum == minumum)
                {
                    return 0f;
                }

                var l = (maximum + minumum) / 2;
                var d = maximum - minumum;

                return l > 0.5f ? d / (2 - maximum - minumum) : d / (maximum + minumum);
            }

            for (var y = 0; y < input.Height; y++)
            {
                for (var x = 0; x < input.Width; x++)
                {
                    var lightness = this.Cie(input[x, y]) / 255f;
                    var sat = Saturation(input[x, y]);

                    var acceptableSaturation = sat > this.Options.SaturationThreshold;
                    var acceptableLightness =
                        lightness >= this.Options.SaturationBrightnessMin &&
                        lightness <= this.Options.SaturationBrightnessMax;

                    var pixel = output[x, y];
                    if (acceptableLightness && acceptableSaturation)
                    {
                        pixel.B = (byte)Math.Min(byte.MaxValue, (sat - this.Options.SaturationThreshold) * (255f / (1f - this.Options.SaturationThreshold)));
                    }
                    else
                    {
                        pixel.B = 0;
                    }
                    output[x, y] = pixel;
                }
            }
        }

        /// <summary>
        /// The DownSample method divides the input image to (factor x factor) sized areas and reduces each of them to one pixel in the output image.
        /// Because not every image can be divided by (factor), the last pixels on the right and the bottom might not be included in the calculation.
        /// </summary>
        private Image<Rgba32> DownSample(Image<Rgba32> input)
        {
            int factor = this.Options.ScoreDownSample;

            // Math.Floor instead of Math.Round to avoid a (factor + 1)th area on the right/bottom
            var width = (int)Math.Floor(input.Width / (float)factor);
            var height = (int)Math.Floor(input.Height / (float)factor);
            var output = new Image<Rgba32>(width, height);
            var ifactor2 = 1f / (factor * factor);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var r = 0;
                    var g = 0;
                    var b = 0;
                    var a = 0;

                    var mr = 0;
                    var mg = 0;

                    for (var v = 0; v < factor; v++)
                    {
                        for (var u = 0; u < factor; u++)
                        {
                            var j = (y * factor + v) * input.Width + (x * factor + u);
                            
                            var argb = input[j % input.Width, j / input.Width];
                            r += argb.R;
                            g += argb.G;
                            b += argb.B;
                            a += argb.A;
                            mr = Math.Max(mr, argb.R);
                            mg = Math.Max(mg, argb.G);
                            // unused
                            // mb = Math.Max(mb, argb.B);
                        }
                    }
                    // this is some funky magic to preserve detail a bit more for
                    // skin (r) and detail (g). Saturation (b) does not get this boost.

                    output[x, y] = new Rgba32(
                        (byte)(r * ifactor2 * 0.5f + mr * 0.5f),
                        (byte)(g * ifactor2 * 0.7f + mg * 0.3f),
                        (byte)(b * ifactor2),
                        (byte)(a * ifactor2));
                }
            }

            return output;
        }

        private float Sample(Image<Rgba32> image, int x, int y)
        {
            return this.Cie(image[x, y]);
        }

        private float Cie(Rgba32 rgb)
        {
            return 0.5126f * rgb.B + 0.7152f * rgb.G + 0.0722f * rgb.R;
        }

        private void ApplyBoosts(Image<Rgba32> output, params BoostArea[] boostAreas)
        {
            for (int y = 0; y < output.Height; y++)
            {
                for (int x = 0; x < output.Width; x++)
                {
                    var pixel = output[x, y];
                    pixel.A = 0;
                    output[x, y] = pixel;
                }
            }

            foreach (var boostArea in boostAreas)
            {
                var x0 = boostArea.Area.X;
                var x1 = boostArea.Area.X + boostArea.Area.Width;
                var y0 = boostArea.Area.Y;
                var y1 = boostArea.Area.Y + boostArea.Area.Height;
                var weight = boostArea.Weight * 255;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var pixel = output[x, y];
                        pixel.A = (byte)Math.Max(0, Math.Min(byte.MaxValue, pixel.A + weight));
                        output[x, y] = pixel;
                    }
                }
            }
        }

        private IReadOnlyList<Crop> GenerateCrops(int width, int height)
        {
            var results = new List<Crop>();
            var minDimension = Math.Min(width, height);
            var cropWidth = this.Options.CropWidth ?? minDimension;
            var cropHeight = this.Options.CropHeight ?? minDimension;

            for (var scale = this.Options.MaxScale; scale >= this.Options.MinScale; scale -= this.Options.ScaleStep)
            {
                for (var y = 0; y + cropHeight * scale <= height; y += this.Options.Step)
                {
                    for (var x = 0; x + cropWidth * scale <= width; x += this.Options.Step)
                    {
                        results.Add(new Crop(new Rectangle(x, y, (int)Math.Round(cropWidth * scale), (int)Math.Round(cropHeight * scale))));
                    }
                }
            }

            return results;
        }

        private Score Score(Image<Rgba32> output, Rectangle crop, BoostArea[] boostAreas)
        {
            var result = new Score();

            var downSample = this.Options.ScoreDownSample;
            var invDownSample = 1 / (double)downSample;
            var outputHeightDownSample = output.Height * downSample;
            var outputWidthDownSample = output.Width * downSample;
            var outputWidth = output.Width;

            for (var y = 0; y < outputHeightDownSample; y += downSample)
            {
                for (var x = 0; x < outputWidthDownSample; x += downSample)
                {
                    var p = ((int)(y * invDownSample)) * outputWidth + ((int)(x * invDownSample));
                    var pixel = output[p % output.Width, p / output.Width];

                    var i = this.Importance(crop, x, y);
                    var detail = pixel.G / 255f;

                    result.Detail += detail * i;
                    result.Skin += pixel.R / 255f * (detail + this.Options.SkinBias) * i;
                    result.Saturation += pixel.B / 255f * (detail + this.Options.SaturationBias) * i;
                    result.Boost += pixel.A / 255f * i;
                }
            }

            if (boostAreas.Any())
            {
                foreach (var boostArea in boostAreas)
                {
                    if (crop.Contains(boostArea.Area))
                    {
                        continue;
                    }

                    if (boostArea.Area.IntersectsWith(crop))
                    {
                        result.Penalty += boostArea.Weight;
                    }
                }

                result.Penalty /= boostAreas.Length;
            }

            result.Total =
              (result.Detail * this.Options.DetailWeight +
               result.Skin * this.Options.SkinWeight +
               result.Saturation * this.Options.SaturationWeight +
               result.Boost * this.Options.BoostWeight) / (crop.Width * crop.Height);

            result.Total -= result.Total * result.Penalty;

            return result;
        }

        private float Importance(Rectangle crop, float x, float y)
        {
            if (crop.X > x || x >= crop.X + crop.Width || crop.Y > y || y >= crop.Y + crop.Height)
            {
                return this.Options.OutsideImportance;
            }

            x = (x - crop.X) / crop.Width;
            y = (y - crop.Y) / crop.Height;
            var px = Math.Abs(0.5f - x) * 2;
            var py = Math.Abs(0.5f - y) * 2;

            // Distance from edge
            var dx = Math.Max(px - 1.0f + this.Options.EdgeRadius, 0);
            var dy = Math.Max(py - 1.0f + this.Options.EdgeRadius, 0);
            var d = (dx * dx + dy * dy) * this.Options.edgeWeight;
            var s = 1.41f - (float)Math.Sqrt(px * px + py * py);
            if (this.Options.RuleOfThirds)
            {
                s += Math.Max(0, s + d + 0.5f) * 1.2f * (this.Thirds(px) + this.Thirds(py));
            }
            return s + d;
        }

        // Gets value in the range of [0; 1] where 0 is the center of the pictures
        // returns weight of rule of thirds [0; 1]
        private float Thirds(float x)
        {
            x = (((x - 1f / 3f + 1f) % 2f) * 0.5f - 0.5f) * 16;
            return Math.Max(1f - x * x, 0f);
        }
    }
}
