using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Input.Preview;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace EyeApp
{
    public sealed partial class MainPage_circle : Page
    {
        // Трекер взгляда и временные метки
        private GazeInputSourcePreview gazeInputSource;
        private DateTime lastUpdateTime;
        private DateTime lastActivationTime = DateTime.MinValue;

        // Коллекции для хранения состояния (теперь для Polygon)
        private readonly Dictionary<Shape, int> keyMap = new Dictionary<Shape, int>();
        private readonly Dictionary<Shape, DateTime> gazeStartTime = new Dictionary<Shape, DateTime>();

        // Настройки времени
        private readonly TimeSpan activationCooldown = TimeSpan.FromMilliseconds(300);
        private readonly TimeSpan gazeActivationDelay = TimeSpan.FromMilliseconds(200);
        private CancellationTokenSource colorResetCts = new CancellationTokenSource();

        // Сетевой клиент
        private readonly HttpClient httpClient = new HttpClient();

        // Canvas для рисования радиальной клавиатуры
        private readonly Canvas pianoCanvas = new Canvas { Width = 600, Height = 600 };

        // Последняя активированная клавиша
        private Shape lastActivatedKey = null;

        public MainPage_circle()
        {
            InitializeComponent();
            InitializePiano();
            InitializeGazeTracking();
        }

        private void InitializePiano()
        {
            MainCanvas.Children.Add(pianoCanvas);
            DrawKeyboard();
        }

        private void InitializeGazeTracking()
        {
            try
            {
                gazeInputSource = GazeInputSourcePreview.GetForCurrentView();
                gazeInputSource.GazeMoved += OnGazeMoved;
                CheckGazeActivity();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Gaze init error: {ex.Message}");
            }
        }

        private void DrawKeyboard()
        {
            // MIDI-ноты
            int[] whiteNotes = { 60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79, 81 };
            int[] blackNotes = { 61, 63, 66, 68, 70, 73, 75, 78, 80 };

            double canvasSize = pianoCanvas.Width;
            double center = canvasSize / 2;

            double radialThickness = 150;                // толщина кольца
            double outerRadius = center - 10;            // радиус наружного круга (с отступом)
            double innerRadius = outerRadius - radialThickness;

            double gapDeg = 60;                          // пустой сектор между 5 и 7 часами снизу
            double totalSpan = 360 - gapDeg;
            int totalKeys = whiteNotes.Length + blackNotes.Length;
            double stepDeg = totalSpan / (totalKeys - 1);    // положительный шаг по часовой стрелке
            double sweepDeg = stepDeg;

            // Опорный угол начала (7 часов)
            double startClockDeg = 7 * 30;               // 210° по часовой от «12"
            // Преобразуем в тригонометрическую систему (0° вправо, +CCW) с учётом инверсии Y-оси
            double trigStart = startClockDeg - 90;       // θ = clockAngle - 90       // преобразуем в тригонометрическую систему (0° вправо, +CCW)

            // Объединяем все ноты и сортируем по возрастанию для правильного порядка
            var allNotes = whiteNotes.Cast<int>().Concat(blackNotes).OrderBy(n => n).ToList();

            // Рисуем все клавиши по кольцу
            for (int i = 0; i < allNotes.Count; i++)
            {
                int note = allNotes[i];
                bool isBlack = blackNotes.Contains(note);
                Color fill = isBlack ? Colors.Black : Colors.White;
                Color stroke = Colors.Black;

                double centerAngle = trigStart + i * stepDeg;
                double startAngle = centerAngle - sweepDeg / 2;

                DrawSector(note, innerRadius, outerRadius, startAngle, sweepDeg, fill, stroke, isBlack);
            }
        }

        private void DrawSector(int note, double innerR, double outerR, double startAngleDeg, double sweepAngleDeg, Color fillColor, Color strokeColor, bool isBlack = false)
        {
            double center = pianoCanvas.Width / 2;
            double startRad = startAngleDeg * Math.PI / 180.0;
            double endRad = (startAngleDeg + sweepAngleDeg) * Math.PI / 180.0;

            var pts = new PointCollection
            {
                new Point(center + innerR * Math.Cos(startRad), center + innerR * Math.Sin(startRad)),
                new Point(center + outerR * Math.Cos(startRad), center + outerR * Math.Sin(startRad)),
                new Point(center + outerR * Math.Cos(endRad), center + outerR * Math.Sin(endRad)),
                new Point(center + innerR * Math.Cos(endRad), center + innerR * Math.Sin(endRad)),
            };

            var poly = new Polygon
            {
                Points = pts,
                Fill = new SolidColorBrush(fillColor),
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = 1,
                Tag = isBlack ? "black" : "white"
            };

            if (isBlack) Canvas.SetZIndex(poly, 1);

            pianoCanvas.Children.Add(poly);
            keyMap[poly] = note;
        }

        private async void OnGazeMoved(GazeInputSourcePreview sender, GazeMovedPreviewEventArgs args)
        {
            try
            {
                var gazePoint = args.CurrentPoint.EyeGazePosition;
                if (gazePoint == null) return;

                lastUpdateTime = DateTime.Now;

                var rootVisual = Window.Current.Content as FrameworkElement;
                var transform = pianoCanvas.TransformToVisual(rootVisual);
                var position = transform.TransformPoint(new Point(0, 0));

                double x = gazePoint.Value.X - position.X;
                double y = gazePoint.Value.Y - position.Y;

                await ProcessGazeInteraction(x, y);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Gaze processing error: {ex.Message}");
            }
        }

        private async Task ProcessGazeInteraction(double x, double y)
        {
            bool found = false;
            foreach (var key in keyMap.Keys.ToList())
            {
                if (key is Polygon poly)
                {
                    if (IsPointInPolygon(new Point(x, y), poly.Points))
                    {
                        found = true;
                        await HandleKeyHover(poly);
                    }
                    else if (gazeStartTime.ContainsKey(poly))
                    {
                        gazeStartTime.Remove(poly);
                        await ResetKeyColor(poly);
                    }
                }
            }
            if (!found)
            {
                colorResetCts.Cancel();
                lastActivatedKey = null;
            }
        }

        private bool IsPointInPolygon(Point pt, PointCollection pts)
        {
            bool inside = false;
            int cnt = pts.Count;
            for (int i = 0, j = cnt - 1; i < cnt; j = i++)
            {
                var pi = pts[i];
                var pj = pts[j];
                if (((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                    (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private async Task HandleKeyHover(Shape key)
        {
            if (key == lastActivatedKey && (DateTime.Now - lastActivationTime) < activationCooldown)
                return;

            if (!gazeStartTime.ContainsKey(key))
                gazeStartTime[key] = DateTime.Now;
            else if ((DateTime.Now - gazeStartTime[key]) > gazeActivationDelay)
            {
                await ActivateKeyAsync(key);
                gazeStartTime.Remove(key);
                lastActivationTime = DateTime.Now;
            }
        }

        private async Task ActivateKeyAsync(Shape key)
        {
            try
            {
                colorResetCts.Cancel();
                colorResetCts = new CancellationTokenSource();

                var originalFill = ((string)key.Tag == "white") ? Colors.White : Colors.Black;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (lastActivatedKey != null && lastActivatedKey != key)
                        lastActivatedKey.Fill = new SolidColorBrush(
                            ((string)lastActivatedKey.Tag == "white") ? Colors.White : Colors.Black
                        );

                    key.Fill = new SolidColorBrush(Colors.LightBlue);
                });

                await httpClient.GetAsync($"http://localhost:8080/?note={keyMap[key]}");
                Debug.WriteLine($"Sent note: {keyMap[key]}");

                await Task.Delay(activationCooldown, colorResetCts.Token);

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (!colorResetCts.Token.IsCancellationRequested)
                        key.Fill = new SolidColorBrush(originalFill);
                });
            }
            catch (OperationCanceledException)
            {
                // нормальная отмена
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Activation error: {ex.Message}");
            }
            finally
            {
                lastActivatedKey = key;
            }
        }

        private async Task ResetKeyColor(Shape key)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                key.Fill = new SolidColorBrush(
                    ((string)key.Tag == "white") ? Colors.White : Colors.Black
                );
            });
        }

        private async void CheckGazeActivity()
        {
            while (true)
            {
                await Task.Delay(1000);
                if ((DateTime.Now - lastUpdateTime).TotalSeconds > 3)
                    await ResetGazeTracking();
            }
        }

        private async Task ResetGazeTracking()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                gazeCoordinates.Text = "Переподключение..."
            );

            try
            {
                if (gazeInputSource != null)
                    gazeInputSource.GazeMoved -= OnGazeMoved;

                gazeInputSource = GazeInputSourcePreview.GetForCurrentView();
                gazeInputSource.GazeMoved += OnGazeMoved;
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    gazeCoordinates.Text = $"Ошибка: {ex.Message}"
                );
            }
        }
    }
}
