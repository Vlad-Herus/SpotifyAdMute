using SpotifyMute;
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
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WpfHost
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Requester m_Requester;
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            this.ShowInTaskbar = false;
            this.Visibility = Visibility.Hidden;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            m_Requester = new Requester(new WindowInteropHelper(this).Handle);
            m_Requester.StateChanged += M_Requester_StateChanged; ;
        }

        private void M_Requester_StateChanged(State obj)
        {
            this.Dispatcher.Invoke(DispatcherPriority.Normal,
                new Action(() =>
                {
                    textBlock.Text = obj.ToString();
                }));
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            m_Requester.KickOff();

        }
    }
}
