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
    public sealed partial class MainPage : Page
    {
        // Трекер взгляда и временные метки
        private GazeInputSourcePreview gazeInputSource;
        private DateTime lastUpdateTime;
        private DateTime lastActivationTime = DateTime.MinValue;

        // Коллекции для хранения состояния
        private readonly Dictionary<Rectangle, int> keyMap = new Dictionary<Rectangle, int>();
        private readonly Dictionary<Rectangle, DateTime> gazeStartTime = new Dictionary<Rectangle, DateTime>();

        // Настройки времени
        private readonly TimeSpan activationCooldown = TimeSpan.FromMilliseconds(300);
        private readonly TimeSpan gazeActivationDelay = TimeSpan.FromMilliseconds(200);
        private CancellationTokenSource colorResetCts = new CancellationTokenSource();

        // Элементы интерфейса и сетевой клиент
        private readonly HttpClient httpClient = new HttpClient();
        private readonly Canvas pianoCanvas = new Canvas { Width = 780, Height = 200 };
        private Rectangle lastActivatedKey = null;

        // Большая кнопка внизу
        private Rectangle bigKey;
        private readonly TimeSpan bigKeyActivationDelay = TimeSpan.FromMilliseconds(100);

        public MainPage()
        {
            InitializeComponent();
            InitializePiano();
            InitializeGazeTracking();

            Window.Current.SizeChanged += OnWindowSizeChanged; // Подписка на событие
        }

        private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            // Перерисовка клавиатуры при изменении размера окна
            DrawKeyboard_dynamic();
        }

        private void InitializePiano()
        {
            MainCanvas.Children.Clear();
            pianoCanvas.Width = 0; // Сброс ширины перед перерисовкой
            pianoCanvas.Height = 0;

            Canvas.SetLeft(pianoCanvas, 0);
            Canvas.SetTop(pianoCanvas, 0);
            MainCanvas.Children.Add(pianoCanvas);

            // Создаём большую клавишу
            bigKey = new Rectangle
            {
                Tag = "big",
                Fill = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0x40, 0x81)),
                Stroke = new SolidColorBrush(Colors.Gray)
            };
            MainCanvas.Children.Add(bigKey);

            DrawKeyboard_dynamic();
        }

        private void InitializeGazeTracking()
        {
            try
            {
                gazeInputSource = GazeInputSourcePreview.GetForCurrentView();
                //gazeInputSource.GazeMoved += OnGazeMoved;
                gazeInputSource.GazeMoved += OnGazeMoved_dynamic;
                CheckGazeActivity();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Gaze init error: {ex.Message}");
            }
        }

        private void DrawKeyboard_dynamic()
        {
            const double minKeyWidth = 40; // Минимальная ширина клавиши
            const double maxKeyWidth = 60; // Максимальная ширина клавиши
            const double keyHeightRatio = 3.33; // Соотношение высоты к ширине (200/60 ≈ 3.33)

            // Получаем доступную ширину экрана (с учётом отступов)
            double availableWidth = Window.Current.Bounds.Width - 20;
            double keyWidth = Math.Clamp(availableWidth / 25, minKeyWidth, maxKeyWidth);
            double keyHeight = keyWidth * keyHeightRatio;

            int keyCounter = 0;

            // Список MIDI-нот для двух октав (C5 до C7)
            List<int> midiNotes = new List<int>();
            for (int octave = 0; octave < 2; octave++)
            {
                for (int i = 0; i < 12; i++)
                {
                    midiNotes.Add(60 + octave * 12 + i);
                }
            }
            midiNotes.Add(84); // Добавляем C7

            // Очистка предыдущих элементов
            pianoCanvas.Children.Clear();
            keyMap.Clear();

            // Создание клавиш
            /*            foreach (int note in midiNotes)
                        {
                            bool isBlack = IsBlackKey(note); // Проверка, является ли клавиша чёрной
                            var key = new Rectangle
                            {
                                Width = keyWidth,
                                Height = keyHeight,
                                Fill = new SolidColorBrush(isBlack ? Colors.Black : Colors.White),
                                Stroke = new SolidColorBrush(Colors.Gray),
                                Tag = isBlack ? "black" : "white"
                            };

                            Canvas.SetLeft(key, keyCounter * keyWidth);
                            pianoCanvas.Children.Add(key);
                            keyMap[key] = note;

                            // Добавление маркера для нот "до"
                            //if (note % 12 == 0)
                                var marker = new Ellipse
                                {
                                    Width = 6,
                                    Height = 6,
                                    Fill = new SolidColorBrush(Colors.Red),
                                    IsHitTestVisible = false
                                };
                            if (note % 12 == 0)
                            {
                                marker = new Ellipse
                                {
                                    Width = 10,
                                    Height = 10,
                                    Fill = new SolidColorBrush(Colors.Red),
                                    IsHitTestVisible = false
                                };
                            }
                                Canvas.SetLeft(marker, keyCounter * keyWidth + keyWidth / 2 - 4);
                                Canvas.SetTop(marker, keyHeight - 20);
                                Canvas.SetZIndex(marker, 2);
                                pianoCanvas.Children.Add(marker);

                            keyCounter++;
                        }
            */
            foreach (int note in midiNotes)
            {
                bool isBlack = IsBlackKey(note);
                var key = new Rectangle
                {
                    Width = keyWidth,
                    Height = keyHeight,
                    Fill = new SolidColorBrush(isBlack ? Colors.Black : Colors.White),
                    Stroke = new SolidColorBrush(Colors.Gray),
                    Tag = isBlack ? "black" : "white"
                };

                Canvas.SetLeft(key, keyCounter * keyWidth);
                pianoCanvas.Children.Add(key);
                keyMap[key] = note; // Правильное связывание клавиши с нотой

                // Добавление маркера
                var marker = new Ellipse
                {
                    Width = (note % 12 == 0) ? 10 : 6,
                    Height = (note % 12 == 0) ? 10 : 6,
                    Fill = new SolidColorBrush(Colors.Red),
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(marker, keyCounter * keyWidth + keyWidth / 2 - marker.Width / 2);
                Canvas.SetTop(marker, keyHeight - 20);
                Canvas.SetZIndex(marker, 2);
                pianoCanvas.Children.Add(marker);

                keyCounter++;
            }

            // Обновляем размеры основной клавиатуры
            pianoCanvas.Width = keyCounter * keyWidth;
            pianoCanvas.Height = keyHeight;

            // Настраиваем большую клавишу
            double bigKeyHeight = keyWidth;
            double bigKeyMargin = 40; // Отступ от основной клавиатуры

            bigKey.Width = pianoCanvas.Width;
            bigKey.Height = bigKeyHeight;
            Canvas.SetLeft(bigKey, 0);
            Canvas.SetTop(bigKey, pianoCanvas.Height + bigKeyMargin);
            Canvas.SetZIndex(bigKey, 1);

            //pianoCanvas.Children.Add(bigKey);

            MainCanvas.Width = pianoCanvas.Width;
            MainCanvas.Height = pianoCanvas.Height + bigKeyMargin + bigKey.Height;


            // Добавляем в keyMap
            keyMap[bigKey] = 0; // note=0 для REST-запроса
        }

        // Проверка, является ли клавиша чёрной
        private bool IsBlackKey(int note)
        {
            int[] blackNotes = { 1, 3, 6, 8, 10 }; // Позиции чёрных клавиш в октаве
            int position = (note - 60) % 12;
            return blackNotes.Contains(position);
        }


        private async void OnGazeMoved_dynamic(GazeInputSourcePreview sender, GazeMovedPreviewEventArgs args)
        {
            try
            {
                var gazePoint = args.CurrentPoint.EyeGazePosition;
                if (gazePoint == null) return;

                // Получаем координаты относительно MainCanvas
                var rootVisual = Window.Current.Content as FrameworkElement;
                var transform = MainCanvas.TransformToVisual(rootVisual);
                var position = transform.TransformPoint(new Point(0, 0));

                // Абсолютные координаты взгляда внутри MainCanvas
                double relativeX = gazePoint.Value.X - position.X;
                double relativeY = gazePoint.Value.Y - position.Y;

                // Нормализация с учётом текущих размеров MainCanvas
                relativeX = Math.Max(0, Math.Min(MainCanvas.ActualWidth, relativeX));
                relativeY = Math.Max(0, Math.Min(MainCanvas.ActualHeight, relativeY));

                await ProcessGazeInteraction(relativeX, relativeY);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Gaze processing error: {ex.Message}");
            }
        }

        private async Task ProcessGazeInteraction(double x, double y)
        {
            Debug.WriteLine($"Process gaze: {x}, {y}");

            bool foundActiveKey = false;

            foreach (var key in keyMap.Keys)
            {
                double keyX = Canvas.GetLeft(key);
                double keyY = Canvas.GetTop(key);
                double keyRight = keyX + key.Width;
                double keyBottom = keyY + key.Height;

                // Для всех клавиш проверяем обе координаты
                bool isInside = (x >= keyX && x <= keyRight && y >= keyY && y <= keyBottom);

                if (isInside)
                {
                    bool isBigKey = (string)key.Tag == "big";
                    Debug.WriteLine($"Активация клавиши: {keyMap[key]}");
                    foundActiveKey = true;
                    await HandleKeyHover(key, isBigKey);
                }
                else
                {
                    if (gazeStartTime.ContainsKey(key))
                    {
                        gazeStartTime.Remove(key);
                        await ResetKeyColor(key);
                    }
                }
            }

            if (!foundActiveKey)
            {
                colorResetCts.Cancel();
                lastActivatedKey = null;
            }


        }

        private async Task HandleKeyHover(Rectangle key, bool isBigKey = false)
        {
            var activationDelay = isBigKey ? bigKeyActivationDelay : gazeActivationDelay;

            if (key == lastActivatedKey &&
                (DateTime.Now - lastActivationTime) < activationCooldown)
                return;

            if (!gazeStartTime.ContainsKey(key))
            {
                gazeStartTime[key] = DateTime.Now;
            }
            else
            {
                var elapsed = DateTime.Now - gazeStartTime[key];
                if (elapsed > activationDelay) // Используем activationDelay вместо gazeActivationDelay
                {
                    await ActivateKeyAsync(key, isBigKey);
                    gazeStartTime.Remove(key);
                    lastActivationTime = DateTime.Now;
                }
            }
        }

        private async Task ActivateKeyAsync(Rectangle key, bool isBigKey = false)
        {
            try
            {
                colorResetCts.Cancel();
                colorResetCts = new CancellationTokenSource();

                var originalColor = isBigKey
                    ? Color.FromArgb(255, 0xFF, 0x40, 0x81) // Исходный цвет большой клавиши
                    : ((string)key.Tag == "white" ? Colors.White : Colors.Black);

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (lastActivatedKey != null && lastActivatedKey != key)
                    {
                        lastActivatedKey.Fill = new SolidColorBrush(
                            (string)lastActivatedKey.Tag == "big"
                                ? Color.FromArgb(255, 0xFF, 0x40, 0x81)
                                : ((string)lastActivatedKey.Tag == "white" ? Colors.White : Colors.Black)
                        );
                    }
                    key.Fill = new SolidColorBrush(isBigKey ? Colors.Cyan : Colors.LightBlue);
                });

                await httpClient.GetAsync($"http://localhost:8080/?note={keyMap[key]}");
                Debug.WriteLine($"Sent note: {keyMap[key]}");

                await Task.Delay(activationCooldown, colorResetCts.Token);

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (!colorResetCts.Token.IsCancellationRequested)
                    {
                        key.Fill = new SolidColorBrush(originalColor);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Отмена анимации
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

        private async Task ResetKeyColor(Rectangle key)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                key.Fill = new SolidColorBrush(
                    (string)key.Tag == "white" ? Colors.White : Colors.Black
                );
            });
        }

        private async void CheckGazeActivity()
        {
            while (true)
            {
                await Task.Delay(1000);
                if ((DateTime.Now - lastUpdateTime).TotalSeconds > 3)
                {
                    await ResetGazeTracking();
                }
            }
        }

        private async Task ResetGazeTracking()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                gazeCoordinates.Text = "Переподключение...";
            });

            try
            {
                if (gazeInputSource != null)
                {
                    gazeInputSource.GazeMoved -= OnGazeMoved_dynamic;
                }
                gazeInputSource = GazeInputSourcePreview.GetForCurrentView();
                gazeInputSource.GazeMoved += OnGazeMoved_dynamic;
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    gazeCoordinates.Text = $"Ошибка: {ex.Message}";
                });
            }
        }
    }
}