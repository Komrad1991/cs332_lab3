using System;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace cs332_lab3
{
    internal class task3
    {
        public static void Run()
        {
            const int width = 600, height = 600;

            // 1. Создание изображения с белым фоном
            //    (Color.White требует явного преобразования в Rgba32 через ToPixel)
            using var image = new Image<Rgba32>(width, height, new Rgba32(255, 255, 255));

            // Вершины треугольника
            var p0 = new Vector2(100, 500);
            var p1 = new Vector2(500, 500);
            var p2 = new Vector2(300, 100);

            // Цвета вершин — три разных
            var c0 = new Rgba32(255, 0, 0);   // Red
            var c1 = new Rgba32(0, 255, 0);   // Lime
            var c2 = new Rgba32(0, 0, 255);   // Blue

            RasterizeTriangle(image, p0, p1, p2, c0, c1, c2);

            // 2. Сохранение в PNG
            image.Save("triangle.png");
            Console.WriteLine("Готово: triangle.png");
        }

        static void RasterizeTriangle(Image<Rgba32> image,
                                      Vector2 p0, Vector2 p1, Vector2 p2,
                                      Rgba32 c0, Rgba32 c1, Rgba32 c2)
        {
            int imgWidth = image.Width;
            int imgHeight = image.Height;

            // 1. Bounding box
            int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
            int maxX = Math.Min(imgWidth - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
            int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
            int maxY = Math.Min(imgHeight - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));

            // Определитель для барицентрических координат (удвоенная площадь)
            float denom = (p1.Y - p2.Y) * (p0.X - p2.X) + (p2.X - p1.X) * (p0.Y - p2.Y);
            if (MathF.Abs(denom) < 1e-6f) return; // вырожденный треугольник

            // 2. Обход пикселей bbox
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // Центр пикселя
                    float px = x + 0.5f;
                    float py = y + 0.5f;

                    // 3. Барицентрические координаты
                    float w0 = ((p1.Y - p2.Y) * (px - p2.X) + (p2.X - p1.X) * (py - p2.Y)) / denom;
                    float w1 = ((p2.Y - p0.Y) * (px - p2.X) + (p0.X - p2.X) * (py - p2.Y)) / denom;
                    float w2 = 1f - w0 - w1;

                    // 4. Внутри треугольника?
                    if (w0 >= 0f && w1 >= 0f && w2 >= 0f)
                    {
                        // 5. Линейная интерполяция цвета (алгоритм Гуро):
                        //    w0 — вес для вершины p0 с цветом c0,
                        //    w1 — вес для вершины p1 с цветом c1,
                        //    w2 — вес для вершины p2 с цветом c2.
                        byte r = (byte)Math.Clamp((int)(w0 * c0.R + w1 * c1.R + w2 * c2.R), 0, 255);
                        byte g = (byte)Math.Clamp((int)(w0 * c0.G + w1 * c1.G + w2 * c2.G), 0, 255);
                        byte b = (byte)Math.Clamp((int)(w0 * c0.B + w1 * c1.B + w2 * c2.B), 0, 255);

                        // 6. Установка пикселя
                        image[x, y] = new Rgba32(r, g, b);
                    }
                }
            }
        }
    }
}