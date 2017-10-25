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
using System.Windows.Forms;
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
        private bool m_ForceClose = false;
        private NotifyIcon notifyIcon = null;
        public MainWindow()
        {
            InitializeComponent();


            notifyIcon = new NotifyIcon();
            notifyIcon.DoubleClick += NotifyIcon_DoubleClick; ;
            notifyIcon.Icon = IconExtractor.GetMyIcon();
            notifyIcon.Visible = true;

            notifyIcon.ContextMenu = new System.Windows.Forms.ContextMenu(new System.Windows.Forms.MenuItem[] {
                new System.Windows.Forms.MenuItem("Exit", Exit)
            });


            this.Loaded += MainWindow_Loaded;
            this.Closing += MainWindow_Closing;
            this.StateChanged += MainWindow_StateChanged;
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            if (this.Visibility == Visibility.Hidden)
            {
                ShowMe();
            }
            else
            {
                HideMe();
            }
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                HideMe();
                WindowState = WindowState.Normal;
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!m_ForceClose)
                e.Cancel = true;

            HideMe();
        }

        void HideMe()
        {
            this.ShowInTaskbar = false;
            this.Visibility = Visibility.Hidden;
        }

        void ShowMe()
        {
            this.ShowInTaskbar = true;
            this.Visibility = Visibility.Visible;
            WindowState = WindowState.Normal;
            this.Show();
            this.Activate();
            this.Topmost = true;  // important
            this.Topmost = false; // important
            this.Focus();         // important

        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            m_Requester = new Requester(new WindowInteropHelper(this).Handle);
            m_Requester.StateChanged += M_Requester_StateChanged;
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

        void Exit(object sender, EventArgs args)
        {
            m_Requester.Dispose();
            m_ForceClose = true;
            this.Close();
        }
    }
}
