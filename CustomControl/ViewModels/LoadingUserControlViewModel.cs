using Serilog;
using System;
using System.Windows.Threading;

namespace CustomControl.ViewModels
{
    public class LoadingUserControlViewModel : BindableBase, IDialogAware
    {
        private readonly DispatcherTimer _timer;
        private readonly ILogger _logger;
        public DialogCloseListener RequestClose { get; }

        public LoadingUserControlViewModel()
        {
            _logger = Serilog.Log.Logger.ForContext<LoadingUserControlViewModel>();
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(50);
            _timer.Tick += OnTimerTick;
        }

        public bool CanCloseDialog()
        {
            return true;
        }

        public void OnDialogClosed()
        {
            _timer.Stop();
            _logger.Debug("加载对话框已关闭");
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            ProgressValue = 0;
            StatusMessage = "正在初始化系统组件...";
            _timer.Start();
            _logger.Information("加载对话框已打开");
        }

        private double _progressValue;
        public double ProgressValue
        {
            get { return _progressValue; }
            set { SetProperty(ref _progressValue, value); }
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value); }
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (ProgressValue < 30)
            {
                ProgressValue += 1.5;
                StatusMessage = "正在加载配置文件...";
            }
            else if (ProgressValue < 60)
            {
                ProgressValue += 1.2;
                StatusMessage = "正在连接数据库...";
            }
            else if (ProgressValue < 90)
            {
                ProgressValue += 0.8;
                StatusMessage = "正在初始化主界面...";
            }
            else if (ProgressValue < 100)
            {
                ProgressValue += 0.5;
                StatusMessage = "即将完成...";
            }
            else
            {
                _timer.Stop();
                StatusMessage = "加载完成！";
                _logger.Information("加载进度完成");
                RequestClose.Invoke(new DialogResult(ButtonResult.OK));
            }
        }
    }
}
