using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

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

        public Result Crop(byte[] imageBytes, params BoostArea[] boostAreas)
        {
            using (var image = SKBitmap.Decode(imageBytes))
            {
                return this.Crop(image, boostAreas);
            }
        }

        public Result Crop(Stream imageStream, params BoostArea[] boostAreas)
        {
            using (var image = SKBitmap.Decode(imageStream))
            {
                return this.Crop(image, boostAreas);
            }
        }

        public Result Crop(SKBitmap image, params BoostArea[] boostAreas)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            switch (image.Info.ColorType)
            {
                case SKColorType.Bgra8888:
                case SKColorType.Rgba8888:
                    break;
                default:
                    throw new ArgumentException(nameof(image), $"Invalid color type: { image.ColorType }. Only color types { SKColorType.Bgra8888 } and { SKColorType.Rgba8888 } are currently supported.");
            }

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

        private Result Analyze(SKBitmap input, params BoostArea[] boostAreas)
        {
            using (var output = new SKBitmap(input.Width, input.Height, true))
            {
                byte* inputPtr = (byte*)input.GetPixels().ToPointer();
                byte* outputPtr = (byte*)output.GetPixels().ToPointer();

                var result = new Result();

                this.EdgeDetect(inputPtr, input.Info, outputPtr, output.Info);
                this.SkinDetect(inputPtr, input.Info, outputPtr, output.Info);
                this.SaturationDetect(inputPtr, input.Info, outputPtr, output.Info);
                this.ApplyBoosts(outputPtr, output.Info, boostAreas);

                using (var scoreOutput = this.DownSample(outputPtr, output.Info))
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
                        using (var image = SKImage.FromBitmap(output))
                        using (var data = image.Encode())                        
                        {
                            result.DebugInfo = new DebugInfo()
                            {
                                Crops = crops,
                                Options = this.Options,
                                Output = data.ToArray(),
                            };
                        }
                    }

                    return result;
                }
            }
        }

        private void EdgeDetect(byte* input, SKImageInfo inputInfo, byte* output, SKImageInfo outputInfo)
        {
            var w = inputInfo.Width;
            var h = inputInfo.Height;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    float lightness;

                    if (x == 0 || x >= w - 1 || y == 0 || y >= h - 1)
                    {
                        lightness = this.Sample(input, inputInfo.ColorType);
                    }
                    else
                    {
                        lightness =
                            this.Sample(input, inputInfo.ColorType) * 4 -       // pixel * 4
                            this.Sample(input - (w * 4), inputInfo.ColorType) - // above
                            this.Sample(input - 4, inputInfo.ColorType) -       // left
                            this.Sample(input + 4, inputInfo.ColorType) -       // right
                            this.Sample(input + (w * 4), inputInfo.ColorType);  // below
                    }

                    *Green(output, outputInfo.ColorType) = (byte)Math.Min(byte.MaxValue, Math.Max(0, Math.Round(lightness)));

                    input += 4;
                    output += 4;
                }
            }
        }

        private void SkinDetect(byte* input, SKImageInfo inputInfo, byte* output, SKImageInfo outputInfo)
        {
            float SkinColor(byte* pixel)
            {
                var blue = *Blue(pixel, inputInfo.ColorType);
                var green = *Green(pixel, inputInfo.ColorType);
                var red = *Red(pixel, inputInfo.ColorType);
                var mag = (float)Math.Sqrt(red * red + green * green + blue * blue);
                var rd = red / mag - this.Options.SkinColor.red;
                var gd = green / mag - this.Options.SkinColor.green;
                var bd = blue / mag - this.Options.SkinColor.blue;
                var d = (float)Math.Sqrt(rd * rd + gd * gd + bd * bd);
                return 1f - d;
            }

            for (var y = 0; y < inputInfo.Height; y++)
            {
                for (var x = 0; x < inputInfo.Width; x++)
                {
                    var lightness = this.Cie(input, inputInfo.ColorType) / 255f;
                    var skin = SkinColor(input);
                    var isSkinColor = skin > this.Options.SkinThreshold;
                    var isSkinBrightness =
                        lightness >= this.Options.SkinBrightnessMin &&
                        lightness <= this.Options.SkinBrightnessMax;

                    if (isSkinColor && isSkinBrightness)
                    {
                        *Red(output, outputInfo.ColorType) = (byte)Math.Min(byte.MaxValue, (skin - this.Options.SkinThreshold) * (255f / (1f - this.Options.SkinThreshold)));
                    }
                    else
                    {
                        *Red(output, outputInfo.ColorType) = 0;
                    }

                    input += 4;
                    output += 4;
                }
            }
        }

        private void SaturationDetect(byte* input, SKImageInfo inputInfo, byte* output, SKImageInfo outputInfo)
        {
            float Saturation(byte* pixel, SKColorType colorType)
            {
                var blue = *Blue(pixel, colorType);
                var green = *Green(pixel, colorType);
                var red = *Red(pixel, colorType);

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

            for (var y = 0; y < inputInfo.Height; y++)
            {
                for (var x = 0; x < inputInfo.Width; x++)
                {
                    var lightness = this.Cie(input, inputInfo.ColorType) / 255f;
                    var sat = Saturation(input, inputInfo.ColorType);

                    var acceptableSaturation = sat > this.Options.SaturationThreshold;
                    var acceptableLightness =
                        lightness >= this.Options.SaturationBrightnessMin &&
                        lightness <= this.Options.SaturationBrightnessMax;

                    if (acceptableLightness && acceptableSaturation)
                    {
                        *Blue(output, outputInfo.ColorType) = (byte)Math.Min(byte.MaxValue, (sat - this.Options.SaturationThreshold) * (255f / (1f - this.Options.SaturationThreshold)));
                    }
                    else
                    {
                        *Blue(output, outputInfo.ColorType) = 0;
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
        private SKBitmap DownSample(byte* input, SKImageInfo info)
        {
            int factor = this.Options.ScoreDownSample;

            // Math.Floor instead of Math.Round to avoid a (factor + 1)th area on the right/bottom
            var width = (int)Math.Floor(info.Width / (float)factor);
            var height = (int)Math.Floor(info.Height / (float)factor);
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
                            var j = (y * factor + v) * info.Width + (x * factor + u);

                            var pixel = input + 4 * j;
                            r += *Red(pixel, info.ColorType);
                            g += *Green(pixel, info.ColorType);
                            b += *Blue(pixel, info.ColorType);
                            a += *(pixel + 3);
                            mr = Math.Max(mr, *(pixel + 2));
                            mg = Math.Max(mg, *(pixel + 1));
                            // unused
                            // mb = Math.Max(mb, *pixel);
                        }
                    }
                    // this is some funky magic to preserve detail a bit more for
                    // skin (r) and detail (g). Saturation (b) does not get this boost.

                    *Red(outputPtr, info.ColorType) = (byte)(r * ifactor2 * 0.5f + mr * 0.5f);
                    *Green(outputPtr, info.ColorType) = (byte)(g * ifactor2 * 0.7f + mg * 0.3f);
                    *Blue(outputPtr, info.ColorType) = (byte)(b * ifactor2);
                    *Alpha(outputPtr, info.ColorType) = (byte)(a * ifactor2);

                    outputPtr += 4;
                }
            }

            return output;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Sample(byte* ptr, SKColorType colorType)
        {
            return this.Cie(ptr, colorType);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Cie(byte* ptr, SKColorType colorType)
        {
            return 0.5126f * *Blue(ptr, colorType)  // blue
                 + 0.7152f * *Green(ptr, colorType) // green
                 + 0.0722f * *Red(ptr, colorType);  // red
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte* Red(byte* ptr, SKColorType colorType) => colorType == SKColorType.Rgba8888 ? ptr : ptr + 2;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte* Green(byte* ptr, SKColorType colorType) => ptr + 1;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte* Blue(byte* ptr, SKColorType colorType) => colorType == SKColorType.Rgba8888 ? ptr + 2 : ptr;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte* Alpha(byte* ptr, SKColorType colorType) => ptr + 3;

        private void ApplyBoosts(byte* output, SKImageInfo info, params BoostArea[] boostAreas)
        {
            for (int y = 0; y < info.Height; y++)
            {
                for (int x = 0; x < info.Width; x++)
                {
                    *Alpha(output + (y * info.Width + x) * 4, info.ColorType) = 0; //alpha
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
                        var alpha = Alpha(output + (y * info.Width + x) * 4, info.ColorType);
                        *alpha = (byte)Math.Max(0, Math.Min(byte.MaxValue, *alpha + weight));
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
                    var detail = *Green(pixel, output.Info.ColorType) / 255f;

                    result.Detail += detail * i;
                    result.Skin += *Red(pixel, output.Info.ColorType) / 255f * (detail + this.Options.SkinBias) * i;
                    result.Saturation += *Blue(pixel, output.Info.ColorType) / 255f * (detail + this.Options.SaturationBias) * i;
                    result.Boost += *Alpha(pixel, output.Info.ColorType) / 255f * i;
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
