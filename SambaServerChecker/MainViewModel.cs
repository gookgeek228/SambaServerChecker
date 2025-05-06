using GalaSoft.MvvmLight.Command;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;

namespace SambaServerChecker
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private SshClient _ssh;
        private Timer _timer;
        private bool _isConnected;

        private const string ip = "192.168.211.123";
        private const string user = "ivan";
        private const string password = "Tesak228";

        public List<string> SystemStatusLines { get; private set; }
        public List<string> NetworkStatusLines { get; private set; }
        public List<string> CtsStatsLines { get; private set; }
        public List<string> RootDirectoryLines { get; private set; }
        public string ConnectionStatus { get; private set; } = "Не подключено";

        public event PropertyChangedEventHandler PropertyChanged;

        public MainViewModel()
        {
            SystemStatusLines = new List<string>();
            NetworkStatusLines = new List<string>();
            CtsStatsLines = new List<string>();
            RootDirectoryLines = new List<string>();
            ConnectToServer();
        }

        public void ConnectToServer()
        {
            try
            {
                if (_ssh != null && _ssh.IsConnected)
                    _ssh.Disconnect();

                _ssh = new SshClient(ip, user, password);
                _ssh.Connect();

                IsConnected = true;
                ConnectionStatus = $"Подключено к {ip}";
                OnPropertyChanged(nameof(ConnectionStatus));
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
                    CtsStatsLines = GetCtsStats().Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    RootDirectoryLines = GetRootDirectoryInfo().Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();

                    OnPropertyChanged(nameof(CtsStatsLines));
                    OnPropertyChanged(nameof(RootDirectoryLines));
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
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect(host, port, null, null);
                    bool isOpen = result.AsyncWaitHandle.WaitOne(500);
                    client.EndConnect(result);
                    return isOpen ? "Открыт" : "Закрыт";
                }
            }
            catch
            {
                return "Ошибка";
            }
        }

        private string GetCtsStats()
        {
            var sb = new System.Text.StringBuilder();

            try
            {
                sb.AppendLine(_ssh.RunCommand("/usr/share/cts/bin/cts show stats").Result);
            }
            catch (Exception ex)
            {
                return $"Ошибка получения CTS stats: {ex.Message}";
            }

            return sb.ToString();
        }

        private string GetRootDirectoryInfo()
        {
            var sb = new System.Text.StringBuilder();

            try
            {
                sb.AppendLine(_ssh.RunCommand("sudo ls -lh /root").Result);
            }
            catch (Exception ex)
            {
                return $"Ошибка чтения директории /root: {ex.Message}";
            }

            return sb.ToString();
        }

        private bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanAccessNetworkAndSystemTabs));
                }
            }
        }

        public bool CanAccessNetworkAndSystemTabs => IsConnected;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}