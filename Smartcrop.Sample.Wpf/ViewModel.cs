using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Smartcrop.Sample.Wpf
{
    public class ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly Func<string> fileSelector;
        private readonly IImageEncoder encoder = new JpegEncoder() { Quality = 100 };

        private int cropWidth = 100;
        private int cropHeight = 100;
        private string sourceImagePath;
        private ImageSource debugImage;
        private ImageSource croppedImage;
        private string errorText;

        public ViewModel(Func<string> fileSelector)
        {
            this.fileSelector = fileSelector ?? throw new ArgumentNullException(nameof(fileSelector));
            this.SelectImageCommand = new SimpleCommand(this.SelectImage);

            var cropProperties = new[] 
            {
                nameof(this.SourceImagePath),
                nameof(this.CropWidth),
                nameof(this.CropHeight)
            };

            // create a new cropped image whenever one of these properties changes
            this.PropertyChanged += (s, e) =>
            {
                if (cropProperties.Any(p => p == e.PropertyName))
                {
                    this.Crop();
                }
            };
        }

        private void Crop()
        {
            try
            {
                // create options and image crop 
                var options = new Options(this.CropWidth, this.CropHeight)
                {
                    Debug = true
                };

                var crop = new ImageCrop(options);

                // load the source image
                using (var image = Image.Load(this.SourceImagePath))
                {   
                    // calculate the best crop area
                    var result = crop.Crop(image);

                    this.DebugImage = this.CreateImageSource(result.DebugInfo.Output);

                    // crop the image
                    image.Mutate(o => o.Crop(result.Area));

                    this.CroppedImage = this.CreateImageSource(image);
                }

                this.ErrorText = null;
            }
            catch (Exception e)
            {
                this.ErrorText = e.Message;
            }
        }

        private ImageSource CreateImageSource(Image<Rgba32> image)
        {
            using (var stream = new MemoryStream())
            {
                image.Save(stream, this.encoder);
                stream.Seek(0, SeekOrigin.Begin);

                var imageSource = new BitmapImage();
                imageSource.BeginInit();
                imageSource.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                imageSource.CacheOption = BitmapCacheOption.OnLoad;
                imageSource.StreamSource = stream;
                imageSource.EndInit();
                imageSource.Freeze();

                return imageSource;
            }
        }

        private void SelectImage()
        {
            try
            {
                var imagePath = this.fileSelector();
                if (imagePath != null)
                {
                    this.SourceImagePath = imagePath;
                }
            }
            catch (Exception e)
            {
                this.ErrorText = e.Message;
            }
        }

        public ICommand SelectImageCommand { get; }

        public int CropWidth
        {
            get => this.cropWidth;
            set => this.SetProperty(ref this.cropWidth, value);
        }

        public int CropHeight
        {
            get => this.cropHeight;
            set => this.SetProperty(ref this.cropHeight, value);
        }

        public string SourceImagePath
        {
            get => this.sourceImagePath;
            set => this.SetProperty(ref this.sourceImagePath, value);
        }

        public ImageSource DebugImage
        {
            get => this.debugImage;
            set => this.SetProperty(ref this.debugImage, value);
        }

        public ImageSource CroppedImage
        {
            get => this.croppedImage;
            set => this.SetProperty(ref this.croppedImage, value);
        }

        public string ErrorText
        {
            get => this.errorText;
            set => this.SetProperty(ref this.errorText, value);
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName]string propertyName = "")
        {
            if (!EqualityComparer<T>.Default.Equals(value, field))
            {
                field = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
