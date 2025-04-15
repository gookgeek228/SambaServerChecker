using GalaSoft.MvvmLight.Command;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;

namespace SambaServerChecker
{
    public class MainViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private SshClient _ssh;
        private Timer _timer;
        private bool _isConnected;

        private string _serverIp;
        private string _username;
        private string _password;
        public ICommand ConnectCommand { get; }


        public string ServerIp
        {
            get => _serverIp;
            set { _serverIp = value; OnPropertyChanged(); }
        }

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); }
        }

        public List<string> SystemStatusLines { get; private set; }
        public List<string> NetworkStatusLines { get; private set; }

        public string ConnectionStatus { get; private set; } = "Не подключено";

        public event PropertyChangedEventHandler PropertyChanged;

        public MainViewModel()
        {
            ConnectCommand = new RelayCommand(ConnectToServer);
            LoadSavedCredentials();
            SystemStatusLines = new List<string>();
            NetworkStatusLines = new List<string>();
        }

        private void LoadSavedCredentials()
        {
            ServerIp = Properties.Settings.Default.LastServerIp;
            Username = Properties.Settings.Default.LastUsername;
            Password = Properties.Settings.Default.LastPassword;

            if (!string.IsNullOrEmpty(ServerIp))
            {
                ConnectToServer();
            }
        }

        public void ConnectToServer()
        {
            try
            {
                Password = ((MainWindow)Application.Current.MainWindow).PasswordBox.Password;

                if (_ssh != null && _ssh.IsConnected)
                    _ssh.Disconnect();

                _ssh = new SshClient(ServerIp, Username, Password);
                _ssh.Connect();

                IsConnected = true;
                ConnectionStatus = $"Подключено к {ServerIp}";
                OnPropertyChanged(nameof(ConnectionStatus));
                SaveCredentials();

                InitializeTimer();
            }
            catch (Exception ex)
            {
                IsConnected = false;
                ConnectionStatus = $"Ошибка подключения: {ex.Message}";
                OnPropertyChanged(nameof(ConnectionStatus));
                StopTimer();
            }
        }

        private void SaveCredentials()
        {
            Properties.Settings.Default.LastServerIp = ServerIp;
            Properties.Settings.Default.LastUsername = Username;
            Properties.Settings.Default.LastPassword = Password;
            Properties.Settings.Default.Save();
        }

        private void InitializeTimer()
        {
            _timer = new Timer(3000);
            _timer.Elapsed += async (s, e) => await UpdateAll();
            _timer.Start();
        }

        private void StopTimer()
        {
            _timer?.Stop();
            _timer?.Dispose();
        }

        private async Task UpdateAll()
        {
            await Task.Run(() =>
            {
                if (_ssh != null && _ssh.IsConnected)
                {
                    SystemStatusLines = GetSystemInfo().Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    NetworkStatusLines = GetNetworkInfo().Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();

                    OnPropertyChanged(nameof(SystemStatusLines));
                    OnPropertyChanged(nameof(NetworkStatusLines));
                }
            });
        }

        private string GetSystemInfo()
        {
            var sb = new System.Text.StringBuilder();

            try
            {
                sb.AppendLine("=== ДИСКИ ===");
                sb.AppendLine(_ssh.RunCommand("df -h").Result);

                sb.AppendLine("\n=== ПАМЯТЬ ===");
                sb.AppendLine(_ssh.RunCommand("free -h").Result);

                sb.AppendLine("\n=== ПРОЦЕССЫ ===");
                sb.AppendLine(_ssh.RunCommand("top -bn1 | head -n 10").Result);
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"Ошибка: {ex.Message}");
            }

            return sb.ToString();
        }

        private string GetNetworkInfo()
        {
            var sb = new System.Text.StringBuilder();
            var host = _ssh.ConnectionInfo.Host;

            sb.AppendLine("=== ПОРТЫ ===");
            sb.AppendLine($"445 (SMB): {CheckPort(host, 445)}");
            sb.AppendLine($"5432 (PostgreSQL): {CheckPort(host, 5432)}");
            sb.AppendLine($"22 (SSH): {CheckPort(host, 22)}");

            sb.AppendLine("\n=== PING ===");
            sb.AppendLine(_ssh.RunCommand("ping -c 2 localhost").Result);

            return sb.ToString();
        }

        private string CheckPort(string host, int port)
        {
            try
            {
                var client = new System.Net.Sockets.TcpClient();
                var result = client.BeginConnect(host, port, null, null);
                bool isOpen = result.AsyncWaitHandle.WaitOne(500);
                client.Close(); 
                return isOpen ? "Открыт" : "Закрыт";
            }
            catch
            {
                return "Ошибка";
            }
        }
        private bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAccessNetworkAndSystemTabs));
            }
        }

        public bool CanAccessNetworkAndSystemTabs => IsConnected;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}