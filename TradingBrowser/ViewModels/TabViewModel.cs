using System;

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

        private string _url = "tradingview.com";
        public string Url 
        { 
            get => _url; 
            set { _url = value; OnPropertyChanged(); } 
        }

        private bool _isPinned;
        public bool IsPinned 
        { 
            get => _isPinned; 
            set { _isPinned = value; OnPropertyChanged(); } 
        }
    }
}
