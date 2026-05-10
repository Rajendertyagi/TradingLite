using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TradingBrowser.ViewModels
{
    public class TabViewModel : ViewModelBase
    {
        public Guid Id { get; } = Guid.NewGuid();
        
        private string _title = "New Tab";
        public string Title 
        { 
            get => _title; 
            set { _title = value; OnPropertyChanged(); } 
        }

        private string _url = "homemarket://";
        public string Url 
        { 
            get => _url; 
            set { _url = value; OnPropertyChanged(); } 
        }

        private string _faviconUrl = "";
        public string FaviconUrl 
        { 
            get => _faviconUrl; 
            set { _faviconUrl = value; OnPropertyChanged(); } 
        }

        private bool _isPinned;
        public bool IsPinned 
        { 
            get => _isPinned; 
            set { _isPinned = value; OnPropertyChanged(); } 
        }
    }

    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
