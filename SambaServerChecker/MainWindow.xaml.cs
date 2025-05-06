using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SambaServerChecker
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Отправляем прокрутку в родительский ScrollViewer, если нужно
            var scrollViewer = FindParentScrollViewer((ScrollViewer)sender);
            if (scrollViewer != null)
            {
                double offset = e.Delta > 0 ? -40 : 40; // Настройка величины прокрутки
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + offset);
                e.Handled = true;
            }
        }

        private ScrollViewer FindParentScrollViewer(DependencyObject child)
        {
            while (child != null)
            {
                if (child is ScrollViewer)
                    return (ScrollViewer)child;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }


    }
}
