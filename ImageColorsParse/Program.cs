using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

class Program
{
    static void Main(string[] args)
    {
        Bitmap originalImage = new Bitmap("C:\\Users\\Roman\\Desktop\\Upscales.ai_1708181879689.jpg");

        Bitmap newImage = GenerateSimilarImage(originalImage, 40, 100, 5);

        newImage.Save("C:\\Users\\Roman\\Desktop\\Звёздное_небо.jpg");

        Console.WriteLine("Новое изображение сохранено.");
        List<Color> pixels = GetPixels(newImage);

        List<Color> clusteredColors = KMeansClustering(pixels, 40);

        int[,] segmentMap = CreateSegmentMap(newImage, clusteredColors);

        List<Color> uniqueColors = GetUniqueColors(segmentMap, clusteredColors);

        Console.WriteLine("Уникальные цвета в новом изображении:");
        foreach (Color color in uniqueColors)
        {
            Console.WriteLine($"Color: {color}");
        }
    }
    static List<Color> GetUniqueColors(int[,] segmentMap, List<Color> colors)
    {
        List<Color> uniqueColors = new List<Color>();

        for (int x = 0; x < segmentMap.GetLength(0); x++)
        {
            for (int y = 0; y < segmentMap.GetLength(1); y++)
            {
                int segmentNumber = segmentMap[x, y];
                Color color = colors[segmentNumber - 1];
                if (!uniqueColors.Contains(color))
                {
                    uniqueColors.Add(color);
                }
            }
        }

        return uniqueColors;
    }
    static Bitmap ApplyGaussianBlur(Bitmap image, int radius)
    {
        Bitmap blurredImage = new Bitmap(image.Width, image.Height);

        using (Graphics g = Graphics.FromImage(blurredImage))
        {
            g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height),
                        new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        }
        double[,] kernel = GenerateGaussianKernel(radius);

        BitmapData imageData = blurredImage.LockBits(new Rectangle(0, 0, blurredImage.Width, blurredImage.Height),
                                                     ImageLockMode.ReadWrite, blurredImage.PixelFormat);
        int bytesPerPixel = Bitmap.GetPixelFormatSize(blurredImage.PixelFormat) / 8;
        int byteCount = imageData.Stride * blurredImage.Height;
        byte[] pixels = new byte[byteCount];
        IntPtr ptrFirstPixel = imageData.Scan0;
        Marshal.Copy(ptrFirstPixel, pixels, 0, pixels.Length);

        int heightInPixels = imageData.Height;
        int widthInBytes = imageData.Width * bytesPerPixel;

        for (int y = 0; y < heightInPixels; y++)
        {
            int currentLine = y * imageData.Stride;
            for (int x = 0; x < widthInBytes; x += bytesPerPixel)
            {
                double[] color = { 0, 0, 0 };
                for (int i = 0; i < kernel.GetLength(0); i++)
                {
                    for (int j = 0; j < kernel.GetLength(1); j++)
                    {
                        int pixelOffset = currentLine + x + (j - radius / 2) * bytesPerPixel;
                        if (pixelOffset >= 0 && pixelOffset < pixels.Length)
                        {
                            color[0] += pixels[pixelOffset] * kernel[i, j];
                            color[1] += pixels[pixelOffset + 1] * kernel[i, j];
                            color[2] += pixels[pixelOffset + 2] * kernel[i, j];
                        }
                    }
                }

                int resultOffset = currentLine + x;
                if (resultOffset >= 0 && resultOffset + 2 < pixels.Length)
                {
                    pixels[resultOffset] = (byte)color[0];
                    pixels[resultOffset + 1] = (byte)color[1];
                    pixels[resultOffset + 2] = (byte)color[2];
                }
            }
        }

        Marshal.Copy(pixels, 0, ptrFirstPixel, pixels.Length);
        blurredImage.UnlockBits(imageData);

        return blurredImage;
    }

    static double[,] GenerateGaussianKernel(int radius)
    {
        int size = 2 * radius + 1;
        double[,] kernel = new double[size, size];
        double sigma = radius / 3.0;
        double sumTotal = 0;

        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                double distance = x * x + y * y;
                double weight = Math.Exp(-distance / (2 * sigma * sigma)) / (2 * Math.PI * sigma * sigma);
                kernel[x + radius, y + radius] = weight;
                sumTotal += weight;
            }
        }

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                kernel[x, y] = kernel[x, y] * 1.0 / sumTotal;
            }
        }

        return kernel;
    }

    static Bitmap GenerateSimilarImage(Bitmap originalImage, int numberOfColors, int mergeThreshold, int blurRadius)
    {
        Bitmap blurredImage = ApplyGaussianBlur(originalImage, blurRadius);

        List<Color> pixels = GetPixels(blurredImage);

        List<Color> clusteredColors = KMeansClustering(pixels, numberOfColors);

        int[,] segmentMap = CreateSegmentMap(blurredImage, clusteredColors);

        MergeSmallObjects(segmentMap, mergeThreshold);

        Bitmap newImage = CreateImageWithMergedObjects(blurredImage, segmentMap, clusteredColors);

        return newImage;
    }

    static List<Color> GetPixels(Bitmap image)
    {
        List<Color> pixels = new List<Color>();
        for (int x = 0; x < image.Width; x++)
        {
            for (int y = 0; y < image.Height; y++)
            {
                pixels.Add(image.GetPixel(x, y));
            }
        }
        return pixels;
    }

    static List<Color> KMeansClustering(List<Color> pixels, int k)
    {
        Random random = new Random();
        List<Color> clusteredColors = pixels.OrderBy(x => random.Next()).Take(k).ToList();
        return clusteredColors;
    }

    static int[,] CreateSegmentMap(Bitmap image, List<Color> colors)
    {
        int[,] segmentMap = new int[image.Width, image.Height];
        for (int x = 0; x < image.Width; x++)
        {
            for (int y = 0; y < image.Height; y++)
            {
                Color pixelColor = image.GetPixel(x, y);
                segmentMap[x, y] = colors.IndexOf(GetNearestColor(pixelColor, colors)) + 1;
            }
        }
        return segmentMap;
    }

    static Color GetNearestColor(Color targetColor, List<Color> colors)
    {
        Color nearestColor = Color.Empty;
        double minDistance = double.MaxValue;

        foreach (Color color in colors)
        {
            double distance = ColorDistance(targetColor, color);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearestColor = color;
            }
        }

        return nearestColor;
    }

    static double ColorDistance(Color color1, Color color2)
    {
        double rDistance = color1.R - color2.R;
        double gDistance = color1.G - color2.G;
        double bDistance = color1.B - color2.B;

        return Math.Sqrt(rDistance * rDistance + gDistance * gDistance + bDistance * bDistance);
    }

    static void MergeSmallObjects(int[,] segmentMap, int threshold)
    {
        int width = segmentMap.GetLength(0);
        int height = segmentMap.GetLength(1);
        int[,] newSegmentMap = new int[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int currentSegment = segmentMap[x, y];
                if (currentSegment == 0)
                {
                    newSegmentMap[x, y] = 0; // Не меняем фон
                    continue;
                }

                List<int> neighborSegments = GetNeighborSegments(segmentMap, x, y);
                int segmentCount = neighborSegments.Count + 1;

                if (segmentCount >= threshold)
                {
                    newSegmentMap[x, y] = currentSegment;
                }
                else
                {
                    int mergedSegment = neighborSegments.FirstOrDefault(seg => seg != 0);
                    if (mergedSegment == 0)
                    {
                        mergedSegment = currentSegment;
                    }
                    newSegmentMap[x, y] = mergedSegment;
                }
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                segmentMap[x, y] = newSegmentMap[x, y];
            }
        }
    }

    static List<int> GetNeighborSegments(int[,] segmentMap, int x, int y)
    {
        List<int> neighborSegments = new List<int>();

        int width = segmentMap.GetLength(0);
        int height = segmentMap.GetLength(1);

        for (int i = Math.Max(0, x - 1); i <= Math.Min(x + 1, width - 1); i++)
        {
            for (int j = Math.Max(0, y - 1); j <= Math.Min(y + 1, height - 1); j++)
            {
                if (i == x && j == y)
                    continue;

                neighborSegments.Add(segmentMap[i, j]);
            }
        }

        return neighborSegments;
    }

    static Bitmap CreateImageWithMergedObjects(Bitmap originalImage, int[,] segmentMap, List<Color> colors)
    {
        Bitmap newImage = new Bitmap(originalImage.Width, originalImage.Height);

        Dictionary<Color, int> colorIndexMap = new Dictionary<Color, int>();
        for (int i = 0; i < colors.Count; i++)
        {
            colorIndexMap[colors[i]] = i + 1; // Индексация начинается с 1
        }

        for (int x = 0; x < originalImage.Width; x++)
        {
            for (int y = 0; y < originalImage.Height; y++)
            {
                int segmentNumber = segmentMap[x, y];
                Color color = colors[segmentNumber - 1];
                newImage.SetPixel(x, y, color);

                // Выводим номера цветов, которые используются для отрисовки
                int colorIndex = colorIndexMap[color];
                using (Graphics g = Graphics.FromImage(newImage))
                {
                    g.DrawString(colorIndex.ToString(), new Font("Arial", 10), Brushes.White, new PointF(x, y));
                }
            }
        }
        return newImage;
    }
}