using GalaSoft.MvvmLight.Command;
using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Input;

namespace SambaServerChecker
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private SshClient _ssh;
        private Timer _timer;
        private Timer _reconnectTimer; // Таймер для переподключения
        private bool _isConnected;
        private string _reqId;
        private bool _isUpdating;
        private bool _disposed;

        private const string ip = "192.168.194.123";
        private const string user = "ivan";
        private const string password = "Tesak228";

        private static readonly HttpClient httpClient = new HttpClient();

        public List<string> SystemStatusLines { get; private set; }
        public List<string> NetworkStatusLines { get; private set; }
        public List<string> CtsStatsLines { get; private set; }
        public List<string> RootDirectoryLines { get; private set; }
        public List<string> RequestStatus { get; private set; }
        public string ConnectionStatus { get; private set; } = "Не подключено";
        public bool CanAccessNetworkAndSystemTabs => IsConnected;

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected == value) return;
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAccessNetworkAndSystemTabs));
            }
        }

        public string ReqId
        {
            get => _reqId;
            set => SetField(ref _reqId, value);
        }

        public ICommand FetchRequestStatusCommand => new RelayCommand(FetchRequestStatus);
        public ICommand ReconnectCommand => new RelayCommand(Reconnect);

        public event PropertyChangedEventHandler PropertyChanged;

        public MainViewModel()
        {
            InitializeCollections();
            ConnectToServer();
            InitializeReconnectTimer(); // Инициализация таймера переподключения
        }

        private void InitializeCollections()
        {
            SystemStatusLines = new List<string>();
            NetworkStatusLines = new List<string>();
            CtsStatsLines = new List<string>();
            RootDirectoryLines = new List<string>();
            RequestStatus = new List<string>();
            ReqId = string.Empty;
        }

        public void ConnectToServer()
        {
            try
            {
                DisposeSshClient();

                _ssh = new SshClient(ip, user, password);
                _ssh.Connect();

                UpdateConnectionStatus(true, $"Подключено к {ip}");
                InitializeTimer();
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus(false, $"Ошибка подключения: {ex.Message}");
            }
        }

        private void InitializeTimer()
        {
            _timer = new Timer(3000);
            _timer.Elapsed += async (s, e) => await SafeUpdate();
            _timer.AutoReset = true;
            _timer.Start();
        }

        private void InitializeReconnectTimer()
        {
            _reconnectTimer = new Timer(10000);
            _reconnectTimer.Elapsed += async (s, e) => await TryReconnect();
            _reconnectTimer.AutoReset = true;
        }

        private async Task TryReconnect()
        {
            if (_isUpdating || IsConnected) return;

            try
            {
                await Task.Delay(2000);
                ConnectToServer();
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus(false, $"Ошибка при переподключении: {ex.Message}");
            }
        }

        private async Task SafeUpdate()
        {
            if (_isUpdating || !IsConnected) return;

            try
            {
                _isUpdating = true;
                await UpdateAll();
            }
            catch (SshConnectionException ex)
            {
                HandleDisconnection($"Ошибка SSH: {ex.Message}");
            }
            catch (SocketException ex)
            {
                HandleDisconnection($"Сетевая ошибка: {ex.Message}");
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private async Task UpdateAll()
        {
            await Task.Run(() =>
            {
                if (!CheckSshConnection()) return;

                SystemStatusLines = GetSystemInfo()
                    .Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

                NetworkStatusLines = GetNetworkInfo()
                    .Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

                CtsStatsLines = GetCtsStats()
                    .Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

                RootDirectoryLines = GetRootDirectoryInfo()
                    .Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

                OnPropertiesChanged();
            });
        }

        private bool CheckSshConnection()
        {
            if (_ssh?.IsConnected == true) return true;

            HandleDisconnection("Соединение потеряно");
            return false;
        }

        private string GetSystemInfo()
        {
            var sb = new System.Text.StringBuilder();

            try
            {
                sb.AppendLine("●      ДИСКИ      ●");
                sb.AppendLine(_ssh.RunCommand("df -h").Result);

                sb.AppendLine("\n●      ПАМЯТЬ      ●");
                sb.AppendLine(_ssh.RunCommand("free -h").Result);

                sb.AppendLine("\n●      ПРОЦЕССЫ      ●");
                sb.AppendLine(_ssh.RunCommand("top -bn1 | head -n 10").Result);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Ошибка: {ex.Message}");
            }

            return sb.ToString();
        }

        private string GetNetworkInfo()
        {
            var sb = new System.Text.StringBuilder();
            var host = _ssh.ConnectionInfo.Host;

            sb.AppendLine("●      ПОРТЫ      ●");
            sb.AppendLine($"445 (SMB): {CheckPortAsync(host, 445).Result}");
            sb.AppendLine($"5432 (PostgreSQL): {CheckPortAsync(host, 5432).Result}");
            sb.AppendLine($"22 (SSH): {CheckPortAsync(host, 22).Result}");

            sb.AppendLine("\n●      PING      ●");
            try
            {
                using (var cmd = _ssh.CreateCommand("ping -c 2 localhost"))
                {
                    sb.AppendLine(cmd.Execute());
                }
            }
            catch
            {
                sb.AppendLine("Ошибка выполнения ping");
            }

            return sb.ToString();
        }

        private async Task<string> CheckPortAsync(string host, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(host, port);
                    await Task.WhenAny(connectTask, Task.Delay(500));
                    return client.Connected ? "Открыт" : "Закрыт";
                }
            }
            catch
            {
                return "Ошибка";
            }
        }

        private string GetCtsStats()
        {
            try
            {
                using (var cmd = _ssh.CreateCommand("/usr/share/cts/bin/cts show stats"))
                {
                    return cmd.Execute();
                }
            }
            catch
            {
                return "Ошибка получения CTS stats";
            }
        }

        private string GetRootDirectoryInfo()
        {
            try
            {
                using (var cmd = _ssh.CreateCommand("sudo ls -lh /root"))
                {
                    return cmd.Execute();
                }
            }
            catch
            {
                return "Ошибка чтения директории /root";
            }
        }

        private async void FetchRequestStatus()
        {
            if (string.IsNullOrWhiteSpace(ReqId))
            {
                SetRequestStatus(new List<string> { "Введите reqId" });
                return;
            }

            if (!CheckSshConnection()) return;

            try
            {
                using (var cmd = _ssh.CreateCommand($"curl -s http://172.26.11.66:8900/v2/requests/{ReqId}/status"))
                {
                    var result = await Task.Factory.FromAsync(cmd.BeginExecute(), cmd.EndExecute);
                    SetRequestStatus(result.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList());
                }
            }
            catch (Exception ex)
            {
                SetRequestStatus(new List<string> { $"Ошибка: {ex.Message}" });
            }
        }

        private void Reconnect()
        {
            if (_isUpdating) return;

            try
            {
                StopTimer();
                ConnectToServer();
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus(false, $"Ошибка переподключения: {ex.Message}");
            }
        }

        private void HandleDisconnection(string message)
        {
            StopTimer();
            _reconnectTimer?.Start(); // Запуск таймера переподключения
            UpdateConnectionStatus(false, message);
        }

        private void UpdateConnectionStatus(bool isConnected, string message)
        {
            IsConnected = isConnected;
            ConnectionStatus = message;
            OnPropertyChanged(nameof(ConnectionStatus));
        }

        private void SetRequestStatus(List<string> status)
        {
            RequestStatus = status;
            OnPropertyChanged(nameof(RequestStatus));
        }

        private void OnPropertiesChanged()
        {
            OnPropertyChanged(nameof(SystemStatusLines));
            OnPropertyChanged(nameof(NetworkStatusLines));
            OnPropertyChanged(nameof(CtsStatsLines));
            OnPropertyChanged(nameof(RootDirectoryLines));
        }

        private void StopTimer()
        {
            _timer?.Stop();
            _timer?.Dispose();
        }

        private void DisposeSshClient()
        {
            if (_ssh == null) return;

            if (_ssh.IsConnected)
            {
                _ssh.Disconnect();
            }
            _ssh.Dispose();
            _ssh = null;
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopTimer();
            _reconnectTimer?.Stop();
            DisposeSshClient();
            httpClient?.Dispose();
            _disposed = true;
        }

        protected void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnPropertyChanged(propertyName);
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
