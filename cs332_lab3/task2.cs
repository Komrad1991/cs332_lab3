using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace cs332_lab3
{
    internal class task2
    {
        /// <summary>
        /// Рисует отрезок целочисленным алгоритмом Брезенхема.
        /// </summary>
        public static void DrawLineBresenham(int x0, int y0, int x1, int y1, Action<int, int, double> setPixel)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;

            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;

            int err = dx + dy;

            while (true)
            {
                setPixel(x0, y0, 1.0);

                if (x0 == x1 && y0 == y1)
                    break;

                int e2 = 2 * err;

                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        /// <summary>
        /// Рисует отрезок алгоритмом Сяолина Ву (со сглаживанием).
        /// </summary>
        public static void DrawLineWu(double x0, double y0, double x1, double y1, Action<int, int, double> setPixel)
        {
            bool steep = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);

            if (steep)
            {
                Swap(ref x0, ref y0);
                Swap(ref x1, ref y1);
            }
            if (x0 > x1)
            {
                Swap(ref x0, ref x1);
                Swap(ref y0, ref y1);
            }

            double dx = x1 - x0;
            double dy = y1 - y0;
            double gradient = dx == 0.0 ? 1.0 : dy / dx;

            // Обработка первой точки
            int xend = (int)Math.Round(x0);
            double yend = y0 + gradient * (xend - x0);
            double xgap = rfpart(x0 + 0.5);
            int xpxl1 = xend;
            int ypxl1 = ipart(yend);

            if (steep)
            {
                setPixel(ypxl1, xpxl1, rfpart(yend) * xgap);
                setPixel(ypxl1 + 1, xpxl1, fpart(yend) * xgap);
            }
            else
            {
                setPixel(xpxl1, ypxl1, rfpart(yend) * xgap);
                setPixel(xpxl1, ypxl1 + 1, fpart(yend) * xgap);
            }

            double intery = yend + gradient;

            // Обработка второй точки
            xend = (int)Math.Round(x1);
            yend = y1 + gradient * (xend - x1);
            xgap = fpart(x1 + 0.5);
            int xpxl2 = xend;
            int ypxl2 = ipart(yend);

            if (steep)
            {
                setPixel(ypxl2, xpxl2, rfpart(yend) * xgap);
                setPixel(ypxl2 + 1, xpxl2, fpart(yend) * xgap);
            }
            else
            {
                setPixel(xpxl2, ypxl2, rfpart(yend) * xgap);
                setPixel(xpxl2, ypxl2 + 1, fpart(yend) * xgap);
            }

            // Основной цикл
            if (steep)
            {
                for (int x = xpxl1 + 1; x <= xpxl2 - 1; x++)
                {
                    setPixel(ipart(intery), x, rfpart(intery));
                    setPixel(ipart(intery) + 1, x, fpart(intery));
                    intery += gradient;
                }
            }
            else
            {
                for (int x = xpxl1 + 1; x <= xpxl2 - 1; x++)
                {
                    setPixel(x, ipart(intery), rfpart(intery));
                    setPixel(x, ipart(intery) + 1, fpart(intery));
                    intery += gradient;
                }
            }
        }

        public static void Run(int bresX0, int bresY0, int bresX1, int bresY1,
                               int wuX0, int wuY0, int wuX1, int wuY1)
        {
            int width = 200;
            int height = 200;
            int thickness = 2; // Толщина линии

            using (Bitmap bmp = new Bitmap(width, height))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                }

                Action<int, int, double> setPixel = (x, y, brightness) =>
                {
                    for (int dx = 0; dx < thickness; dx++)
                    {
                        for (int dy = 0; dy < thickness; dy++)
                        {
                            int px = x + dx;
                            int py = y + dy;

                            if (px < 0 || px >= width || py < 0 || py >= height)
                                continue;

                            brightness = Math.Max(0.0, Math.Min(1.0, brightness));

                            Color bg = bmp.GetPixel(px, py);

                            int r = (int)(bg.R * (1.0 - brightness));
                            int g = (int)(bg.G * (1.0 - brightness));
                            int b = (int)(bg.B * (1.0 - brightness));

                            bmp.SetPixel(px, py, Color.FromArgb(255, r, g, b));
                        }
                    }
                };

                DrawLineBresenham(bresX0, bresY0, bresX1, bresY1, setPixel);

                DrawLineWu(wuX0, wuY0, wuX1, wuY1, setPixel);

                string outputPath = "output_thick.png";
                bmp.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine($"Готово! Картинка сохранена в: {Path.GetFullPath(outputPath)}");
            }
        }

        // --- Вспомогательные методы ---

        private static void Swap(ref double a, ref double b)
        {
            double temp = a;
            a = b;
            b = temp;
        }

        private static int ipart(double x) => (int)Math.Floor(x);

        private static double fpart(double x) => x - Math.Floor(x);

        private static double rfpart(double x) => 1.0 - fpart(x);
    }
}