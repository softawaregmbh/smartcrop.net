using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Smartcrop
{
    public unsafe class ImageCrop
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

        public Result Crop(SKBitmap image, params BoostArea[] boostAreas)
        {
            //using (var image = SKBitmap.Decode(stream))
            {
                SKBitmap resizedImage = null;

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
                                resizedImage = image.Resize(new SKImageInfo((int)Math.Round(image.Width * prescale), (int)Math.Round(image.Height * prescale)), SKFilterQuality.Medium);

                                this.Options.CropWidth = (int)Math.Round(this.Options.CropWidth.Value * (double)prescale);
                                this.Options.CropHeight = (int)Math.Round(this.Options.CropHeight.Value * (double)prescale);

                                for (int i = 0; i < boostAreas.Length; i++)
                                {
                                    var area = boostAreas[i].Area;
                                    boostAreas[i].Area =
                                        new Rectangle(
                                            (int)Math.Round(area.X * prescale),
                                            (int)Math.Round(area.Y * prescale),
                                            (int)Math.Round(area.Width * prescale),
                                            (int)Math.Round(area.Height * prescale));
                                }
                            }
                            else
                            {
                                prescale = 1;
                            }
                        }
                    }

                    var result = this.Analyze(resizedImage ?? image, boostAreas);

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
                    resizedImage?.Dispose();
                }
            }
        }

        private Result Analyze(SKBitmap input, params BoostArea[] boostAreas)
        {
            var output = new SKBitmap(input.Width, input.Height, true);

            byte* inputPtr = (byte*)input.GetPixels().ToPointer();
            byte* outputPtr = (byte*)output.GetPixels().ToPointer();

            try
            {
                var result = new Result();

                this.EdgeDetect(inputPtr, outputPtr, input.Width, input.Height);
                this.SkinDetect(inputPtr, outputPtr, input.Width, input.Height);
                this.SaturationDetect(inputPtr, outputPtr, input.Width, input.Height);
                this.ApplyBoosts(outputPtr, output.Width, output.Height, boostAreas);

                using (var scoreOutput = this.DownSample(outputPtr, output.Width, output.Height))
                {
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
            }
            finally
            {
                output?.Dispose();
            }
        }

        private void Save(SKBitmap bitmap)
        {
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode())
            using (var stream = File.OpenWrite("debug.png"))
            {
                data.SaveTo(stream);
            }
        }

        private void EdgeDetect(byte* input, byte* output, int width, int height)
        {
            var w = width;
            var h = height;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    float lightness;

                    byte r = *(output + 2);

                    if (x == 0 || x >= w - 1 || y == 0 || y >= h - 1)
                    {
                        lightness = this.Sample(input);
                    }
                    else
                    {
                        lightness =
                            this.Sample(input) * 4 -           // pixel * 4
                            this.Sample(input - (width * 4)) - // above
                            this.Sample(input - 4) -           // left
                            this.Sample(input + 4) -           // right
                            this.Sample(input + (width * 4));  // below
                    }

                    *(output + 1) = (byte)Math.Min(byte.MaxValue, Math.Max(0, Math.Round(lightness))); //green

                    byte r2 = *(output + 2);
                    if (r != r2)
                    {
                        Debugger.Break();
                    }

                    input += 4;
                    output += 4;
                }
            }
        }

        private void SkinDetect(byte* input, byte* output, int width, int height)
        {
            float SkinColor(byte* pixel)
            {
                var blue = *pixel;
                var green = *(pixel + 1);
                var red = *(pixel + 2);
                var mag = (float)Math.Sqrt(red * red + green * green + blue * blue);
                var rd = red / mag - this.Options.SkinColor.red;
                var gd = green / mag - this.Options.SkinColor.green;
                var bd = blue / mag - this.Options.SkinColor.blue;
                var d = (float)Math.Sqrt(rd * rd + gd * gd + bd * bd);
                return 1f - d;
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var lightness = this.Cie(input) / 255f;
                    var skin = SkinColor(input);
                    var isSkinColor = skin > this.Options.SkinThreshold;
                    var isSkinBrightness =
                        lightness >= this.Options.SkinBrightnessMin &&
                        lightness <= this.Options.SkinBrightnessMax;

                    if (isSkinColor && isSkinBrightness)
                    {
                        *(output + 2) = (byte)Math.Min(byte.MaxValue, (skin - this.Options.SkinThreshold) * (255f / (1f - this.Options.SkinThreshold))); // red
                    }
                    else
                    {
                        *(output + 2) = 0; // red
                    }

                    input += 4;
                    output += 4;
                }
            }
        }

        private void SaturationDetect(byte* input, byte* output, int width, int height)
        {
            float Saturation(byte* pixel)
            {
                var blue = *pixel;
                var green = *(pixel + 1);
                var red = *(pixel + 2);

                var maximum = Math.Max(red / 255f, Math.Max(green / 255f, blue / 255f));
                var minumum = Math.Min(red / 255f, Math.Min(green / 255f, blue / 255f));

                if (maximum == minumum)
                {
                    return 0f;
                }

                var l = (maximum + minumum) / 2;
                var d = maximum - minumum;

                return l > 0.5f ? d / (2 - maximum - minumum) : d / (maximum + minumum);
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var lightness = this.Cie(input) / 255f;
                    var sat = Saturation(input);

                    var acceptableSaturation = sat > this.Options.SaturationThreshold;
                    var acceptableLightness =
                        lightness >= this.Options.SaturationBrightnessMin &&
                        lightness <= this.Options.SaturationBrightnessMax;

                    if (acceptableLightness && acceptableSaturation)
                    {
                        *output = (byte)Math.Min(byte.MaxValue, (sat - this.Options.SaturationThreshold) * (255f / (1f - this.Options.SaturationThreshold))); // blue
                    }
                    else
                    {
                        *output = 0; //blue
                    }

                    input += 4;
                    output += 4;
                }
            }
        }

        /// <summary>
        /// The DownSample method divides the input image to (factor x factor) sized areas and reduces each of them to one pixel in the output image.
        /// Because not every image can be divided by (factor), the last pixels on the right and the bottom might not be included in the calculation.
        /// </summary>
        private SKBitmap DownSample(byte* input, int inputWidth, int inputHeight)
        {
            int factor = this.Options.ScoreDownSample;

            // Math.Floor instead of Math.Round to avoid a (factor + 1)th area on the right/bottom
            var width = (int)Math.Floor(inputWidth / (float)factor);
            var height = (int)Math.Floor(inputHeight / (float)factor);
            var output = new SKBitmap(width, height);

            byte* outputPtr = (byte*)output.GetPixels().ToPointer();

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
                            var j = (y * factor + v) * inputWidth + (x * factor + u);

                            var pixel = input + 4 * j;
                            r += *(pixel + 2);
                            g += *(pixel + 1);
                            b += *pixel;
                            a += *(pixel + 3);
                            mr = Math.Max(mr, *(pixel + 2));
                            mg = Math.Max(mg, *(pixel + 1));
                            // unused
                            // mb = Math.Max(mb, *pixel);
                        }
                    }
                    // this is some funky magic to preserve detail a bit more for
                    // skin (r) and detail (g). Saturation (b) does not get this boost.

                    *outputPtr++ = (byte)(b * ifactor2);
                    *outputPtr++ = (byte)(g * ifactor2 * 0.7f + mg * 0.3f);
                    *outputPtr++ = (byte)(r * ifactor2 * 0.5f + mr * 0.5f);
                    *outputPtr++ = (byte)(a * ifactor2);
                }
            }

            return output;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Sample(byte* ptr)
        {
            return this.Cie(ptr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Cie(byte* ptr)
        {
            return 0.5126f * *(ptr)      // blue
                 + 0.7152f * *(ptr + 1)  // green
                 + 0.0722f * *(ptr + 2); // red
        }

        private void ApplyBoosts(byte* output, int width, int height, params BoostArea[] boostAreas)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    *(output + y * width + x * 4 + 3) = 0; //alpha
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
                        var alphaPtr = output + y * width + x * 4 + 3;
                        *alphaPtr = (byte)Math.Max(0, Math.Min(byte.MaxValue, *alphaPtr + weight)); //alpha
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

        private Score Score(SKBitmap output, Rectangle crop, BoostArea[] boostAreas)
        {
            var result = new Score();

            var downSample = this.Options.ScoreDownSample;
            var invDownSample = 1 / (double)downSample;
            var outputHeightDownSample = output.Height * downSample;
            var outputWidthDownSample = output.Width * downSample;
            var outputWidth = output.Width;

            var ptr = (byte*)output.GetPixels().ToPointer();

            for (var y = 0; y < outputHeightDownSample; y += downSample)
            {
                for (var x = 0; x < outputWidthDownSample; x += downSample)
                {
                    var pixel = ptr + (((int)(y * invDownSample)) * outputWidth + ((int)(x * invDownSample))) * 4;

                    var i = this.Importance(crop, x, y);
                    var detail = *(pixel + 1) / 255f;

                    result.Detail += detail * i;
                    result.Skin += *(pixel + 2) / 255f * (detail + this.Options.SkinBias) * i;
                    result.Saturation += *pixel / 255f * (detail + this.Options.SaturationBias) * i;
                    result.Boost += *(pixel + 3) / 255f * i;
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Thirds(float x)
        {
            x = (((x - 1f / 3f + 1f) % 2f) * 0.5f - 0.5f) * 16;
            return Math.Max(1f - x * x, 0f);
        }
    }
}
