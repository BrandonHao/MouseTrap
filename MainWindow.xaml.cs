using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Timers;
using System.Windows.Media;
using System.Windows.Threading;
using DYMO.Label.Framework;
namespace MouseTrap
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SerialPort _dataPort;

        private string stringData = "";

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
            Title = "Mouse Trap QR Print";
            var ports = SerialPort.GetPortNames();
            ComSelector.Items.Clear();
            _label = Framework.Open(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\Barcode.Label");
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
                    VoltBox.Background = Brushes.White;
                    LotBox.Background = Brushes.White;
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
                    int maxVoltage = Int32.MaxValue;
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
                        Dispatcher.Invoke(() => LotBox.Background = Brushes.Red);
                        MessageBox.Show("Please specify a lot number!", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    if (minVoltage < measuredVoltage && measuredVoltage < maxVoltage)
                    {
                        ILabelWriterPrintParams printParams = null;
                        ILabelWriterPrinter labelWriterPrinter = _printer as ILabelWriterPrinter;
                        _label.SetObjectText("TEXT", macAddress);
                        macAddress += "+" + lotNum;
                        _label.SetObjectText("BARCODE", macAddress);
                        _label.Print(_printer, null);
                    }
                    else
                    {
                        Dispatcher.Invoke(() => VoltBox.Background = Brushes.Red);
                    }
                }
                catch (FormatException)
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
