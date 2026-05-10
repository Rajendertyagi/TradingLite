using System;
using System.IO;
using System.Windows.Input;

namespace TradingBrowser.ViewModels
{
    public class DownloadItem : ViewModelBase
    {
        public string FileName { get; set; } = "Downloading...";
        public string FilePath { get; set; } = "";
        
        private long _bytesReceived;
        public long BytesReceived 
        { 
            get => _bytesReceived; 
            set { _bytesReceived = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); } 
        }

        private long _totalBytes;
        public long TotalBytes 
        { 
            get => _totalBytes; 
            set { _totalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); } 
        }

        public int Progress => TotalBytes <= 0 ? 0 : (int)((BytesReceived * 100) / TotalBytes);

        private bool _isComplete;
        public bool IsComplete 
        { 
            get => _isComplete; 
            set { _isComplete = value; OnPropertyChanged(); } 
        }

        public ICommand OpenFileCommand { get; set; }
        public ICommand OpenFolderCommand { get; set; }

        public DownloadItem()
        {
            OpenFileCommand = new RelayCommand(_ => 
            {
                if (File.Exists(FilePath)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FilePath) { UseShellExecute = true });
            });
            OpenFolderCommand = new RelayCommand(_ => 
            {
                if (File.Exists(FilePath)) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{FilePath}\"");
            });
        }
    }
}
