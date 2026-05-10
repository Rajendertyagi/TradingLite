using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace TradingBrowser.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        public ObservableCollection<TabViewModel> Tabs { get; set; }
        
        private TabViewModel? _selectedTab;
        public TabViewModel? SelectedTab
        {
            get => _selectedTab;
            set
            {
                _selectedTab = value;
                OnPropertyChanged();
                // Update address bar text when switching tabs
                if (_selectedTab != null) AddressBarText = _selectedTab.Url;
            }
        }

        private string _addressBarText = "";
        public string AddressBarText
        {
            get => _addressBarText;
            set { _addressBarText = value; OnPropertyChanged(); }
        }

        // Tells the View to actually trigger the WebView2 navigation
        public event Action<TabViewModel, string>? RequestNavigate;

        public ICommand AddTabCommand { get; }
        public ICommand NavigateCommand { get; }
        public ICommand CloseTabCommand { get; }

        public MainViewModel()
        {
            Tabs = new ObservableCollection<TabViewModel>();
            AddTabCommand = new RelayCommand(_ => AddTab());
            NavigateCommand = new RelayCommand(_ => ExecuteNavigate());
            CloseTabCommand = new RelayCommand(param => CloseTab(param as TabViewModel));

            AddTab();
        }

        private void AddTab()
        {
            var newTab = new TabViewModel();
            Tabs.Add(newTab);
            SelectedTab = newTab;
        }

        private void CloseTab(TabViewModel? tab)
        {
            if (tab == null) return;
            Tabs.Remove(tab);
            if (SelectedTab == tab) SelectedTab = Tabs.Count > 0 ? Tabs[Tabs.Count - 1] : null;
        }

        public void ExecuteNavigate()
        {
            if (SelectedTab == null || string.IsNullOrWhiteSpace(AddressBarText)) return;

            string input = AddressBarText.Trim();
            string url;

            // SPEC: TradingView-specific shortcuts (e.g., type tv BTCUSD)
            if (input.StartsWith("tv ", StringComparison.OrdinalIgnoreCase))
            {
                string symbol = input.Substring(3).Trim();
                url = $"https://www.tradingview.com/chart/?symbol={Uri.EscapeDataString(symbol)}";
            }
            // SPEC: Auto-detect if URL or Search Query
            else if (input.Contains(".") && !input.Contains(" "))
            {
                url = input.StartsWith("http") ? input : "https://" + input;
            }
            else
            {
                // Default Search Engine (Google)
                url = $"https://www.google.com/search?q={Uri.EscapeDataString(input)}";
            }

            RequestNavigate?.Invoke(SelectedTab, url);
        }
    }
}
