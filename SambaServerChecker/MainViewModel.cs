using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace SambaServerChecker
{
    public class MainViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly Renci.SshNet.SshClient _ssh;
        private readonly System.Timers.Timer _timer;
        private string _systemStatus = "Подключаемся...";
        private string _networkStatus = "Проверка сети...";

        public List<string> SystemStatusLines { get; private set; }
        public List<string> NetworkStatusLines { get; private set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;



        public string SystemStatus
        {
            get => _systemStatus;
            set { _systemStatus = value; OnPropertyChanged(); }
        }

        public string NetworkStatus
        {
            get => _networkStatus;
            set { _networkStatus = value; OnPropertyChanged(); }
        }

        public MainViewModel()
        {
            
            _ssh = new Renci.SshNet.SshClient("192.168.58.124", "ivan", "Tesak228");

            try
            {
                _ssh.Connect();
            }
            catch (System.Exception ex)
            {
                SystemStatus = $"Ошибка подключения: {ex.Message}";
            }

            _timer = new System.Timers.Timer(3000);
            _timer.Elapsed += async (s, e) => await UpdateAll();
            _timer.Start();
        }

        private async System.Threading.Tasks.Task UpdateAll()
        {
            await System.Threading.Tasks.Task.Run(() =>
            {
                SystemStatusLines = GetSystemInfo().Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                NetworkStatusLines = GetNetworkInfo().Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                OnPropertyChanged(nameof(SystemStatusLines));
                OnPropertyChanged(nameof(NetworkStatusLines));
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

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }
}