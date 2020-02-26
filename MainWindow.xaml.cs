using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Timers;
using System.Drawing.Imaging;
using System.Drawing;
using System.Windows.Threading;
using DYMO.Label.Framework;
using ZXing;
using System.Windows.Input;

namespace MouseTrap
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SerialPort _dataPort;

        private string stringData = "";
        private string _appPath;

        private Stopwatch _timeoutStopwatch;

        private ILabel _label;

        private IPrinter _printer;
        /// <summary>
        /// The <c>Timer</c> used to time input. Lasts for 1 second and does not repeat until more data is sent.
        /// </summary>
        private Timer t = new Timer(1000);

        public MainWindow()
        {
            InitializeComponent();
            Dispatcher.UnhandledException += DispatcherOnUnhandledException;
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.KeyDownEvent, new KeyEventHandler(OnKeyPress), true);

            Title = "Mouse Trap QR Print";
            string _appPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var ports = SerialPort.GetPortNames();
            ComSelector.Items.Clear();
            if (!Directory.Exists(_appPath + "\\temp"))
            {
                Directory.CreateDirectory(_appPath + "\\temp");
            }

            _label = Framework.Open(_appPath + "\\Barcode.Label");
            foreach (var printer in Framework.GetPrinters())
            {
                _printer = printer;
            }

            foreach (var name in ports)
            {
                ComSelector.Items.Add(name);
            }

            _dataPort = new SerialPort();

            _timeoutStopwatch = new Stopwatch();
        }

        private void DispatcherOnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            using (StreamWriter outputFile = new StreamWriter(Path.Combine(path, "stacktrace.txt")))
            {
                outputFile.WriteLine(e.Exception.StackTrace);
                outputFile.WriteLine(e.Exception.ToString());
                outputFile.WriteLine(e.Exception.Message);
                outputFile.WriteLine(e.Exception.Source);
            }
        }

        private void Connect_Button_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectButton.Content.Equals("Connect"))
            {
                try
                {
                    _dataPort.PortName = ComSelector.Text;
                    _dataPort.Parity = Parity.None;
                    _dataPort.DataBits = 8;
                    _dataPort.StopBits = StopBits.One;
                    _dataPort.Handshake = Handshake.None;
                    _dataPort.ReadTimeout = 200;
                    _dataPort.WriteTimeout = 50;
                    _dataPort.Open();
                    ConnectButton.Content = "Disconnect";
                    t.Elapsed += Output;
                    _dataPort.DataReceived += DataReceivedHandler;
                }
                //Catch errors
                catch (IOException)
                {
                    ConnectButton.IsEnabled = true;
                    MessageBox.Show("Port Not Found!", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (ArgumentException)
                {
                    ConnectButton.IsEnabled = true;
                    MessageBox.Show("Port Not Found!", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (UnauthorizedAccessException)
                {
                    ConnectButton.IsEnabled = true;
                    MessageBox.Show("Com Port Already In Use!", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                _dataPort.Close();
                ConnectButton.Content = "Connect";
            }

        }

        private void Exit_Button_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void OnKeyPress(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F4)
            {
                MinVoltBox.IsEnabled = !MinVoltBox.IsEnabled;
                MaxVoltBox.IsEnabled = !MaxVoltBox.IsEnabled;
                if(MinVoltBox.Visibility == Visibility.Hidden)
                {
                    MinVoltBox.Visibility = Visibility.Visible;
                    MaxVoltBox.Visibility = Visibility.Visible;
                    MinVoltBlock.Visibility = Visibility.Visible;
                    MaxVoltBlock.Visibility = Visibility.Visible;
                    DescBlock.Visibility = Visibility.Visible;
                }
                else
                {
                    MinVoltBox.Visibility = Visibility.Hidden;
                    MaxVoltBox.Visibility = Visibility.Hidden;
                    MinVoltBlock.Visibility = Visibility.Hidden;
                    MaxVoltBlock.Visibility = Visibility.Hidden;
                    DescBlock.Visibility = Visibility.Hidden;
                }
            }
        }

        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            int dataLength = _dataPort.BytesToRead;
            byte[] data = new byte[dataLength];
            int nbrDataRead = _dataPort.Read(data, 0, dataLength);
            if (nbrDataRead == 0)
            {
                return;
            }
            WriteData(data);
        }

        private void WriteData(byte[] input)
        {
            if (stringData.Equals(string.Empty))
            {
                t.Enabled = true;
                Dispatcher?.Invoke(() =>
                {
                    VoltBox.Background = System.Windows.Media.Brushes.White;
                    LotBox.Background = System.Windows.Media.Brushes.White;
                    MACAddressBox.Text = string.Empty;
                    VoltBox.Text = string.Empty;
                });
            }

            stringData += System.Text.Encoding.ASCII.GetString(input).Trim().Replace(" ", "");
        }

        private void Output(object source, ElapsedEventArgs e)
        {
            t.Enabled = false;
            try
            {
                int startIndex = stringData.IndexOf("MACADDRESS:") + "MACADDRESS:".Length;
                int endIndex = stringData.IndexOf("BATT:") + "BATT:".Length;
                string voltage = stringData.Substring(endIndex);
                string macAddress = stringData.Substring(startIndex, endIndex - startIndex - "BATT:".Length);
                string minVoltString = "";
                Dispatcher?.Invoke(() => minVoltString = MinVoltBox.Text);

                string maxVoltString = "";
                Dispatcher?.Invoke(() => maxVoltString = MaxVoltBox.Text);

                string lotNum = "";
                Dispatcher?.Invoke(() => lotNum = LotBox.Text);

                Dispatcher?.Invoke(() =>
                {
                    MACAddressBox.Text = macAddress;
                    VoltBox.Text = voltage;
                });

                macAddress = macAddress.Trim();
                macAddress = macAddress.Replace("\r", "");
                macAddress = macAddress.Replace("\n", "");
                var arr = macAddress.Split(':');
                arr = arr.Reverse().ToArray();
                macAddress = string.Join("", arr);
                try
                {
                    int minVoltage = 3000;
                    int maxVoltage = int.MaxValue;
                    int measuredVoltage = Convert.ToInt32(voltage);
                    if (minVoltString != string.Empty)
                    {
                        minVoltage = Convert.ToInt32(minVoltString);
                    }

                    if (maxVoltString != string.Empty)
                    {
                        maxVoltage = Convert.ToInt32(maxVoltString);
                    }

                    if (lotNum.Equals(string.Empty))
                    {
                        Dispatcher.Invoke(() => LotBox.Background = System.Windows.Media.Brushes.Red);
                        MessageBox.Show("Please specify a lot number!", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    if (minVoltage < measuredVoltage && measuredVoltage < maxVoltage)
                    {
                        ILabelWriterPrinter labelWriterPrinter = _printer as ILabelWriterPrinter;
                        _label.SetObjectText("TEXT", macAddress);
                        macAddress += "+" + lotNum;

                        var QCwriter = new BarcodeWriter();
                        QCwriter.Format = BarcodeFormat.QR_CODE;
                        var result = QCwriter.Write(macAddress);
                        string imagePath = _appPath + "\\temp\\qrcode.png";
                        var barcodeBitmap = new Bitmap(result);
                        using (MemoryStream memory = new MemoryStream())
                        {
                            using (FileStream fs = new FileStream(imagePath,
                               FileMode.Create, FileAccess.ReadWrite))
                            {
                                barcodeBitmap.Save(memory, ImageFormat.Png);
                                byte[] bytes = memory.ToArray();
                                fs.Write(bytes, 0, bytes.Length);
                            }
                        }
                        var imageStream = Application.GetResourceStream(new Uri("qrcode.png", UriKind.Relative)).Stream;

                        _label.SetImagePngData("GRAPHIC", imageStream);
                        _label.SetObjectText("BARCODE", macAddress);
                        _label.Print(_printer, null);
                    }
                    else
                    {
                        Dispatcher.Invoke(() => VoltBox.Background = System.Windows.Media.Brushes.Red);
                    }
                }
                catch (System.FormatException)
                {
                    MessageBox.Show("Specified voltage range was not an integer value!", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                if (stringData.Contains("?"))
                {
                    return;
                }
                MessageBox.Show("The data transmission was interrupted or corrupt! Please try again.", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            stringData = string.Empty;
        }
    }
}
