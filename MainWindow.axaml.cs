using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Input;

namespace FloodFillAvalonia;

public partial class MainWindow : Window
{
    private WorkMode _mode = WorkMode.ColorFill;
    private Color _fillColor = Color.Parse("#2DD4BF");
    private PatternImage? _pattern;
    private bool _drawing;
    private bool _movedWhilePressed;
    private Point _lastPoint;
    private Point _pressPoint;

    public MainWindow()
    {
        InitializeComponent();
        Canvas.PointerPressed += Canvas_PointerPressed;
        Canvas.PointerMoved += Canvas_PointerMoved;
        Canvas.PointerReleased += Canvas_PointerReleased;
        Canvas.ColorTolerance = 10;
        Canvas.Initialize(900, 620, Colors.White);
        UpdateModeUi();
    }


    private void ModeColor_Click(object? sender, RoutedEventArgs e)
    {
        _mode = WorkMode.ColorFill;
        UpdateModeUi();
        StatusText.Text = "1а: нарисуйте замкнутую область и щёлкните внутри неё.";
    }

    private void ModePattern_Click(object? sender, RoutedEventArgs e)
    {
        _mode = WorkMode.PatternFill;
        UpdateModeUi();
        StatusText.Text = "1б: загрузите рисунок, нарисуйте область и щёлкните внутри неё.";
    }

    private void ModeBoundary_Click(object? sender, RoutedEventArgs e)
    {
        _mode = WorkMode.BoundaryTrace;
        UpdateModeUi();
        StatusText.Text = "1в: загрузите изображение и щёлкните по пикселю области или границы. Будет найден периметр связной области.";
    }

    private void ApplyTolerance_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ColorToleranceBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tolerance))
        {
            StatusText.Text = "Допуск должен быть целым числом от 0 до 64.";
            return;
        }

        if (tolerance < 0 || tolerance > 64)
        {
            StatusText.Text = "Допуск должен быть в диапазоне от 0 до 64.";
            return;
        }

        Canvas.ColorTolerance = tolerance;
        StatusText.Text = $"Допуск цвета установлен: ±{tolerance} на каждый канал.";
    }

    private void ApplyColor_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            _fillColor = Color.Parse(ColorHexBox.Text ?? string.Empty);
            StatusText.Text = $"Цвет заливки: #{_fillColor.R:X2}{_fillColor.G:X2}{_fillColor.B:X2}.";
        }
        catch
        {
            StatusText.Text = "Неверный цвет. Используйте формат #RRGGBB или #AARRGGBB.";
        }
    }

    private void PresetRed_Click(object? sender, RoutedEventArgs e) => SetPreset("#EF4444");
    private void PresetBlue_Click(object? sender, RoutedEventArgs e) => SetPreset("#3B82F6");
    private void PresetGreen_Click(object? sender, RoutedEventArgs e) => SetPreset("#22C55E");

    private void SetPreset(string value)
    {
        _fillColor = Color.Parse(value);
        ColorHexBox.Text = value;
    }

    private async void LoadPattern_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите рисунок для заливки",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Изображения") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] }
            ]
        });

        if (files.Count != 1) return;
        await using var stream = await files[0].OpenReadAsync();
        _pattern = await PatternImage.LoadAsync(stream);
        PatternInfo.Text = $"{files[0].Name}: {_pattern.Width}×{_pattern.Height} px";
        StatusText.Text = _pattern.Width <= 128 && _pattern.Height <= 128
            ? "Загружен небольшой рисунок — можно включить циклическое повторение."
            : "Загружен большой рисунок — масштабирование не выполняется; используется его исходный размер.";
    }

    private async void LoadImage_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите исходное изображение",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Изображения") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] }
            ]
        });

        if (files.Count != 1) return;
        await using var stream = await files[0].OpenReadAsync();
        var loaded = await PatternImage.LoadAsync(stream);
        Canvas.SetPixels(loaded.Width, loaded.Height, loaded.Pixels.ToArray());
        Canvas.ClearBoundary();
        BoundaryInfo.Text = "Точек обхода: 0";
        StatusText.Text = $"Исходное изображение загружено: {loaded.Width}×{loaded.Height}. Щёлкните по области или её границе.";
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        Canvas.Initialize(900, 620, Colors.White);
        BoundaryInfo.Text = "Точек обхода: 0";
        StatusText.Text = "Холст очищен.";
    }

    private void ClearBoundary_Click(object? sender, RoutedEventArgs e)
    {
        Canvas.ClearBoundary();
        BoundaryInfo.Text = "Точек обхода: 0";
        StatusText.Text = "Выделение границы очищено.";
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить результат",
            SuggestedFileName = "result.png",
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
        });

        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        Canvas.SavePng(stream);
        StatusText.Text = "Результат сохранён.";
    }

    private void Canvas_PointerPressed(object? sender, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(Canvas);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;

        var p = Canvas.ToBitmapPoint(point.Position);
        if (!Canvas.IsInside(p)) return;

        if (_mode == WorkMode.BoundaryTrace)
        {
            var trace = Canvas.TraceBoundary(p);
            BoundaryInfo.Text = $"Точек обхода: {trace.Count}";
            StatusText.Text = trace.Count == 0
                ? $"Граница не найдена. Щёлкните по области или её границе. Допуск цвета: ±{Canvas.ColorTolerance}."
                : $"Граница обойдена: {trace.Count:N0} точек. Допуск цвета: ±{Canvas.ColorTolerance}. Она прорисована поверх исходного изображения.";
            return;
        }

        // В 1а/1б различаем рисование и обычный щелчок:
        // если мышь только нажали и отпустили — запускается заливка;
        // если была протяжка — рисуем контур.
        _drawing = true;
        _movedWhilePressed = false;
        _pressPoint = point.Position;
        _lastPoint = point.Position;
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_mode == WorkMode.BoundaryTrace || !_drawing) return;
        var point = e.GetCurrentPoint(Canvas);
        if (!point.Properties.IsLeftButtonPressed) return;

        var current = point.Position;
        if (Math.Abs(current.X - _pressPoint.X) + Math.Abs(current.Y - _pressPoint.Y) > 2)
            _movedWhilePressed = true;

        if (_movedWhilePressed)
        {
            Canvas.DrawLine(_lastPoint, current, Colors.Black, 3);
            _lastPoint = current;
        }
    }

    private void Canvas_PointerReleased(object? sender, PointerEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;

        if (_movedWhilePressed)
        {
            StatusText.Text = "Контур нарисован. Теперь щёлкните внутри области для заливки.";
            return;
        }

        var p = Canvas.ToBitmapPoint(_pressPoint);
        if (!Canvas.IsInside(p)) return;

        Canvas.ClearBoundary();
        if (_mode == WorkMode.ColorFill)
        {
            StatusText.Text = Canvas.FillColorRecursive(p, _fillColor);
        }
        else if (_mode == WorkMode.PatternFill)
        {
            if (_pattern is null)
            {
                StatusText.Text = "Сначала загрузите рисунок для заливки.";
                return;
            }
            StatusText.Text = Canvas.FillPatternRecursive(p, _pattern, PatternRepeatBox.IsChecked == true);
        }
    }

    private void UpdateModeUi()
    {
        ModeText.Text = _mode switch
        {
            WorkMode.ColorFill => "Режим: 1а — цвет",
            WorkMode.PatternFill => "Режим: 1б — рисунок",
            _ => "Режим: 1в — граница"
        };
        ModeColorButton.Classes.Clear();
        ModePatternButton.Classes.Clear();
        ModeBoundaryButton.Classes.Clear();
        switch (_mode)
        {
            case WorkMode.ColorFill: ModeColorButton.Classes.Add("accent"); break;
            case WorkMode.PatternFill: ModePatternButton.Classes.Add("accent"); break;
            case WorkMode.BoundaryTrace: ModeBoundaryButton.Classes.Add("accent"); break;
        }
    }
}
