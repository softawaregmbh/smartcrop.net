# Smartcrop.net

This is a .NET Standard port of Jonas Wagner's [smartcrop.js](https://github.com/jwagner/smartcrop.js) content aware image cropping library. 

## Usage

Install the following nuget package Smartcrop.net (check the "include prerelease" box):

Add the following code:

```csharp
using (var image = File.OpenRead(@"path\to\image.jpg"))
{
    // find best crop
    var result = new ImageCrop(200, 200).Crop(image);

    Console.WriteLine(
        $"Best crop: {result.Area.X}, {result.Area.Y} - {result.Area.Width} x {result.Area.Height}");
}
```

This is a very simple version, all the [options](https://github.com/jwagner/smartcrop.js#cropOptions) from the original project are also available.

### Sample

There is also a [WPF sample project](https://github.com/softawaregmbh/smartcrop.net/tree/master/Smartcrop.Sample.Wpf) (but since this is a .NET Standard library, you can also use it with .NET Core):

![Screenshot of the sample project's UI](https://github.com/softawaregmbh/smartcrop.net/raw/master/sample.png)
Image: https://www.flickr.com/photos/endogamia/5682480447 by Leon F. Cabeiro (N. Feans), licensed under [CC-BY-2.0](https://creativecommons.org/licenses/by/2.0/)
