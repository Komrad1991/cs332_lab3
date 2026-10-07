using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace FloodFillAvalonia;

public sealed class PixelCanvas : Control
{
    private byte[] _pixels = Array.Empty<byte>();
    private WriteableBitmap? _bitmap;
    private int _width;
    private int _height;
    private const int BytesPerPixel = 4;
    private readonly List<Point> _boundary = [];
    // Small tolerance for anti-aliased/compressed images. Two colors are
    // considered equal when every RGBA channel differs by at most this value.
    private int _colorTolerance = 10;

    public int ColorTolerance
    {
        get => _colorTolerance;
        set => _colorTolerance = Math.Clamp(value, 0, 64);
    }

    public int PixelWidth => _width;
    public int PixelHeight => _height;

    public void Initialize(int width, int height, Color background)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        _width = width;
        _height = height;
        _pixels = new byte[width * height * BytesPerPixel];
        FillAll(background);
        RecreateBitmap();
        _boundary.Clear();
        InvalidateVisual();
    }

    public void SetPixels(int width, int height, byte[] bgraPixels)
    {
        if (bgraPixels.Length < width * height * 4)
            throw new ArgumentException("Недостаточно данных пикселей.", nameof(bgraPixels));
        _width = width;
        _height = height;
        _pixels = new byte[width * height * 4];
        Array.Copy(bgraPixels, _pixels, _pixels.Length);
        RecreateBitmap();
        _boundary.Clear();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_bitmap is not null && _width > 0 && _height > 0)
        {
            context.DrawImage(_bitmap, new Rect(0, 0, _width, _height), Bounds);
        }

        if (_boundary.Count > 1)
        {
            var pen = new Pen(Brushes.Magenta, 2);
            for (var i = 1; i < _boundary.Count; i++)
                context.DrawLine(pen, BitmapToControl(_boundary[i - 1]), BitmapToControl(_boundary[i]));

            // The list contains each boundary point once; close the visual contour.
            context.DrawLine(pen, BitmapToControl(_boundary[^1]), BitmapToControl(_boundary[0]));
        }
    }

    public Point ToBitmapPoint(Point controlPoint)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return new Point(-1, -1);
        var x = (int)Math.Floor(controlPoint.X * _width / Bounds.Width);
        var y = (int)Math.Floor(controlPoint.Y * _height / Bounds.Height);
        return new Point(x, y);
    }

    public bool IsInside(Point p) => p.X >= 0 && p.Y >= 0 && p.X < _width && p.Y < _height;

    public void DrawLine(Point controlStart, Point controlEnd, Color color, int width)
    {
        var a = ToBitmapPoint(controlStart);
        var b = ToBitmapPoint(controlEnd);
        DrawLinePixels((int)a.X, (int)a.Y, (int)b.X, (int)b.Y, color, Math.Max(1, width));
        CommitAndInvalidate();
    }

    public string FillColorRecursive(Point start, Color color)
    {
        var x = (int)start.X;
        var y = (int)start.Y;
        if (!IsInside(start)) return "Точка находится вне изображения.";
        var target = GetColor(x, y);
        if (SameColor(target, color)) return "Область уже имеет заданный цвет.";

        var filled = 0L;
        FillSpanRecursive(x, y, target, color, color, ref filled);
        CommitAndInvalidate();
        return $"1а выполнен: заполнено {filled:N0} пикселей.";
    }

    public string FillPatternRecursive(Point start, PatternImage pattern, bool repeat)
    {
        var x = (int)start.X;
        var y = (int)start.Y;
        if (!IsInside(start)) return "Точка находится вне изображения.";
        if (pattern.Width == 0 || pattern.Height == 0) return "Пустой рисунок.";

        var target = GetColor(x, y);
        long filled = 0;
        FillSpanPatternRecursive(x, y, target, pattern, repeat, x, y, ref filled);
        CommitAndInvalidate();
        return repeat
            ? $"1б выполнен: рисунок циклически повторён, обработано {filled:N0} пикселей."
            : $"1б выполнен: рисунок применён без масштабирования, обработано {filled:N0} пикселей.";
    }

    private void FillSpanPatternRecursive(int x, int y, Color target, PatternImage pattern, bool repeat, int originX, int originY, ref long filled)
    {
        if (!IsInside(new Point(x, y)) || !SameColor(GetColor(x, y), target)) return;

        var left = x;
        while (left > 0 && SameColor(GetColor(left - 1, y), target)) left--;
        var right = x;
        while (right + 1 < _width && SameColor(GetColor(right + 1, y), target)) right++;

        for (var px = left; px <= right; px++)
        {
            var patternX = px - originX;
            var patternY = y - originY;
            if (!repeat && (patternX < 0 || patternY < 0 || patternX >= pattern.Width || patternY >= pattern.Height))
                continue;

            if (repeat)
            {
                patternX = Mod(patternX, pattern.Width);
                patternY = Mod(patternY, pattern.Height);
            }

            var i = (patternY * pattern.Width + patternX) * 4;
            SetRaw(px, y, pattern.Pixels[i], pattern.Pixels[i + 1], pattern.Pixels[i + 2], 255);
            filled++;
        }

        ScanNeighborPattern(left, right, y - 1, target, pattern, repeat, originX, originY, ref filled);
        ScanNeighborPattern(left, right, y + 1, target, pattern, repeat, originX, originY, ref filled);
    }

    private void ScanNeighborPattern(int left, int right, int y, Color target, PatternImage pattern, bool repeat, int originX, int originY, ref long filled)
    {
        if (y < 0 || y >= _height) return;
        var x = left;
        while (x <= right)
        {
            while (x <= right && !SameColor(GetColor(x, y), target)) x++;
            if (x > right) break;
            var runStart = x;
            while (x <= right && SameColor(GetColor(x, y), target)) x++;
            FillSpanPatternRecursive(runStart, y, target, pattern, repeat, originX, originY, ref filled);
        }
    }

    public List<Point> TraceBoundary(Point start)
    {
        _boundary.Clear();

        var sx = (int)start.X;
        var sy = (int)start.Y;
        if (!IsInside(start))
            return _boundary;

        // The clicked pixel selects the connected region/boundary color.
        // This is important for 1c after task 1a/1b: clicking inside a solid
        // filled region should still allow us to recover the region's perimeter.
        var selectedColor = GetColor(sx, sy);

        // 1. Find the complete 8-connected component of pixels close enough
        //    to the selected color.
        var component = new bool[_width * _height];
        var queue = new Queue<(int x, int y)>();
        var componentPixels = new List<(int x, int y)>();
        queue.Enqueue((sx, sy));
        component[sy * _width + sx] = true;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            componentPixels.Add((x, y));

            foreach (var (dx, dy) in MooreDirections)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (!IsInsidePixel(nx, ny))
                    continue;

                var index = ny * _width + nx;
                if (component[index])
                    continue;

                if (!SameColor(GetColor(nx, ny), selectedColor))
                    continue;

                component[index] = true;
                queue.Enqueue((nx, ny));
            }
        }

        if (componentPixels.Count == 0)
            return _boundary;

        // 2. Extract the perimeter of the component. We deliberately use the
        //    4-neighborhood here: a component pixel is a boundary pixel when
        //    at least one N/E/S/W neighbor is outside the component.
        //    This turns a solid filled area into a one-pixel contour while still
        //    keeping thin, explicitly drawn boundary lines usable for 1c.
        var boundaryMask = new bool[_width * _height];
        var boundaryPixels = new List<(int x, int y)>();
        foreach (var (x, y) in componentPixels)
        {
            var isBoundary =
                !IsInsidePixel(x - 1, y) || !component[y * _width + (x - 1)] ||
                !IsInsidePixel(x + 1, y) || !component[y * _width + (x + 1)] ||
                !IsInsidePixel(x, y - 1) || !component[(y - 1) * _width + x] ||
                !IsInsidePixel(x, y + 1) || !component[(y + 1) * _width + x];

            if (!isBoundary)
                continue;

            boundaryMask[y * _width + x] = true;
            boundaryPixels.Add((x, y));
        }

        if (boundaryPixels.Count == 0)
        {
            // The selected component fills the whole image, so there is no
            // distinguishable external perimeter inside the image.
            return _boundary;
        }

        // If the user clicked inside a solid region, move the start to the
        // closest perimeter pixel. If the user clicked on the perimeter itself,
        // that pixel is retained.
        var boundaryStart = boundaryMask[sy * _width + sx]
            ? (sx, sy)
            : FindNearestBoundaryPixel(sx, sy, boundaryPixels);

        // 3. Trace the one-pixel boundary with Moore's neighborhood.
        (int x, int y) current = boundaryStart;
        (int x, int y) backtrack = (current.x - 1, current.y);
        var initialState = (current, backtrack);

        _boundary.Add(new Point(current.x, current.y));

        // For a one-pixel contour, repeated (current, backtrack) is the safest
        // fallback termination rule. The expected return to the initial state
        // is checked first.
        var visitedStates = new HashSet<(int x, int y, int bx, int by)>();
        var maxSteps = Math.Max(64, boundaryPixels.Count * 16);

        for (var step = 0; step < maxSteps; step++)
        {
            var state = (current.x, current.y, backtrack.x, backtrack.y);
            if (!visitedStates.Add(state))
                break;

            if (!TryGetNextBoundaryPixel(boundaryMask, current, backtrack, out var next, out var nextBacktrack))
                break;

            current = next;
            backtrack = nextBacktrack;

            if (current == initialState.current && backtrack == initialState.backtrack)
                break;

            // Do not add the closing start pixel twice.
            if (current != boundaryStart)
                _boundary.Add(new Point(current.x, current.y));
        }

        // Degenerate case: a single boundary pixel is still a valid result.
        if (_boundary.Count == 1 && boundaryPixels.Count > 1)
        {
            // The contour is not directly traceable as a cycle (for example a
            // tiny or highly branched component). Fall back to all perimeter
            // pixels in deterministic scan order instead of returning a dot.
            _boundary.Clear();
            foreach (var p in boundaryPixels.OrderBy(p => p.y).ThenBy(p => p.x))
                _boundary.Add(new Point(p.x, p.y));
        }

        InvalidateVisual();
        return _boundary;
    }

    private (int x, int y) FindNearestBoundaryPixel(
        int sx,
        int sy,
        List<(int x, int y)> boundaryPixels)
    {
        var best = boundaryPixels[0];
        var bestDistance = long.MaxValue;

        foreach (var p in boundaryPixels)
        {
            var dx = p.x - sx;
            var dy = p.y - sy;
            var distance = (long)dx * dx + (long)dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = p;
            }
        }

        return best;
    }

    private static readonly (int dx, int dy)[] MooreDirections =
    [
        (1, 0),
        (1, 1),
        (0, 1),
        (-1, 1),
        (-1, 0),
        (-1, -1),
        (0, -1),
        (1, -1)
    ];

    private bool TryGetNextBoundaryPixel(
        bool[] component,
        (int x, int y) current,
        (int x, int y) backtrack,
        out (int x, int y) next,
        out (int x, int y) nextBacktrack)
    {
        var dx = Math.Clamp(backtrack.x - current.x, -1, 1);
        var dy = Math.Clamp(backtrack.y - current.y, -1, 1);
        var backDirection = 4; // West; also safe fallback for an out-of-image backtrack.

        for (var i = 0; i < MooreDirections.Length; i++)
        {
            if (MooreDirections[i].dx == dx && MooreDirections[i].dy == dy)
            {
                backDirection = i;
                break;
            }
        }

        // Scan clockwise, starting immediately after the backtrack pixel.
        for (var k = 1; k <= MooreDirections.Length; k++)
        {
            var direction = (backDirection + k) % MooreDirections.Length;
            var nx = current.x + MooreDirections[direction].dx;
            var ny = current.y + MooreDirections[direction].dy;

            if (!IsInsidePixel(nx, ny) || !component[ny * _width + nx])
                continue;

            var previousDirection = (direction + MooreDirections.Length - 1) % MooreDirections.Length;
            next = (nx, ny);
            nextBacktrack =
            (
                current.x + MooreDirections[previousDirection].dx,
                current.y + MooreDirections[previousDirection].dy
            );
            return true;
        }

        next = current;
        nextBacktrack = backtrack;
        return false;
    }

    private bool IsPerimeterPixel(int x, int y, bool[] component)
    {
        for (var i = 0; i < MooreDirections.Length; i++)
        {
            var nx = x + MooreDirections[i].dx;
            var ny = y + MooreDirections[i].dy;
            if (!IsInsidePixel(nx, ny) || !component[ny * _width + nx])
                return true;
        }

        return false;
    }

    private bool IsInsidePixel(int x, int y) =>
        x >= 0 && y >= 0 && x < _width && y < _height;

    private static int Mod(int value, int modulo) => ((value % modulo) + modulo) % modulo;

    public void ClearBoundary()
    {
        _boundary.Clear();
        InvalidateVisual();
    }

    public void SavePng(Stream stream)
    {
        if (_bitmap is null) return;
        _bitmap.Save(stream);
    }

    private void FillSpanRecursive(int x, int y, Color target, Color fill, Color unused, ref long filled)
    {
        if (!IsInside(new Point(x, y)) || !SameColor(GetColor(x, y), target)) return;

        var left = x;
        while (left > 0 && SameColor(GetColor(left - 1, y), target)) left--;
        var right = x;
        while (right + 1 < _width && SameColor(GetColor(right + 1, y), target)) right++;

        for (var px = left; px <= right; px++)
        {
            SetColor(px, y, fill);
            filled++;
        }

        ScanNeighborSpan(left, right, y - 1, target, fill, ref filled);
        ScanNeighborSpan(left, right, y + 1, target, fill, ref filled);
    }

    private void ScanNeighborSpan(int left, int right, int y, Color target, Color fill, ref long filled)
    {
        if (y < 0 || y >= _height) return;
        var x = left;
        while (x <= right)
        {
            while (x <= right && !SameColor(GetColor(x, y), target)) x++;
            if (x > right) break;
            var runStart = x;
            while (x <= right && SameColor(GetColor(x, y), target)) x++;
            FillSpanRecursive(runStart, y, target, fill, fill, ref filled);
        }
    }

    private void FillSpanPatternRecursive(int x, int y, Color target, PatternImage pattern, bool repeat, ref long filled)
    {
        if (!IsInside(new Point(x, y)) || !SameColor(GetColor(x, y), target)) return;

        var left = x;
        while (left > 0 && SameColor(GetColor(left - 1, y), target)) left--;
        var right = x;
        while (right + 1 < _width && SameColor(GetColor(right + 1, y), target)) right++;

        for (var px = left; px <= right; px++)
        {
            var relativeX = px - left;
            if (repeat || relativeX < pattern.Width)
            {
                var p = relativeX;
                var q = y;
                if (repeat)
                {
                    p %= pattern.Width;
                    q %= pattern.Height;
                }
                else if (q < 0 || q >= pattern.Height)
                {
                    continue;
                }
                SetRaw(px, y, pattern.Pixels[(q * pattern.Width + p) * 4], pattern.Pixels[(q * pattern.Width + p) * 4 + 1], pattern.Pixels[(q * pattern.Width + p) * 4 + 2], 255);
                filled++;
            }
        }

        ScanNeighborPattern(left, right, y - 1, target, pattern, repeat, ref filled);
        ScanNeighborPattern(left, right, y + 1, target, pattern, repeat, ref filled);
    }

    private void ScanNeighborPattern(int left, int right, int y, Color target, PatternImage pattern, bool repeat, ref long filled)
    {
        if (y < 0 || y >= _height) return;
        var x = left;
        while (x <= right)
        {
            while (x <= right && !SameColor(GetColor(x, y), target)) x++;
            if (x > right) break;
            var runStart = x;
            while (x <= right && SameColor(GetColor(x, y), target)) x++;
            FillSpanPatternRecursive(runStart, y, target, pattern, repeat, ref filled);
        }
    }

    private Point BitmapToControl(Point p)
    {
        if (_width == 0 || _height == 0) return new Point();
        return new Point((p.X + 0.5) * Bounds.Width / _width, (p.Y + 0.5) * Bounds.Height / _height);
    }

    private void FillAll(Color color)
    {
        for (var i = 0; i < _pixels.Length; i += 4)
            SetRaw(i / 4 % _width, i / 4 / _width, color.B, color.G, color.R, color.A);
    }

    private Color GetColor(int x, int y)
    {
        var i = (y * _width + x) * 4;
        return Color.FromArgb(_pixels[i + 3], _pixels[i + 2], _pixels[i + 1], _pixels[i]);
    }

    private void SetColor(int x, int y, Color color) => SetRaw(x, y, color.B, color.G, color.R, color.A);

    private void SetRaw(int x, int y, byte b, byte g, byte r, byte a)
    {
        var i = (y * _width + x) * 4;
        _pixels[i] = b;
        _pixels[i + 1] = g;
        _pixels[i + 2] = r;
        _pixels[i + 3] = a;
    }

    private bool SameColor(Color a, Color b) =>
        Math.Abs(a.A - b.A) <= _colorTolerance &&
        Math.Abs(a.R - b.R) <= _colorTolerance &&
        Math.Abs(a.G - b.G) <= _colorTolerance &&
        Math.Abs(a.B - b.B) <= _colorTolerance;

    private void DrawLinePixels(int x0, int y0, int x1, int y1, Color color, int thickness)
    {
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            DrawBrush(x0, y0, color, thickness);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private void DrawBrush(int cx, int cy, Color color, int diameter)
    {
        var radius = Math.Max(0, diameter / 2);
        for (var y = cy - radius; y <= cy + radius; y++)
            for (var x = cx - radius; x <= cx + radius; x++)
                if (IsInside(new Point(x, y))) SetColor(x, y, color);
    }

    private void RecreateBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);
        CommitAndInvalidate();
    }

    private void CommitAndInvalidate()
    {
        if (_bitmap is null) return;
        using var locked = _bitmap.Lock();
        var rowBytes = locked.RowBytes;
        var expected = _width * 4;
        if (rowBytes == expected)
        {
            Marshal.Copy(_pixels, 0, locked.Address, _pixels.Length);
        }
        else
        {
            for (var y = 0; y < _height; y++)
                Marshal.Copy(_pixels, y * expected, IntPtr.Add(locked.Address, y * rowBytes), expected);
        }
        InvalidateVisual();
    }
}
