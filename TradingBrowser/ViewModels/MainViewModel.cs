using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TradingBrowser.Services;
using TradingBrowser.Views;

namespace TradingBrowser.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        public ObservableCollection<TabViewModel> Tabs { get; set; }
        public ObservableCollection<DownloadItem> Downloads { get; set; } = new ObservableCollection<DownloadItem>();
        
        private TabViewModel? _selectedTab;
        public TabViewModel? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab != null) _selectedTab.IsSelected = false;
                _selectedTab = value;
                if (_selectedTab != null) { _selectedTab.IsSelected = true; AddressBarText = _selectedTab.Url; }
                OnPropertyChanged();
            }
        }

        private string _addressBarText = "";
        public string AddressBarText
        {
            get => _addressBarText;
            set { _addressBarText = value; OnPropertyChanged(); }
        }

        private bool _isChartMode;
        public bool IsChartMode { get => _isChartMode; set { _isChartMode = value; OnPropertyChanged(); } }

        private bool _isSiteWhitelisted;
        public bool IsSiteWhitelisted { get => _isSiteWhitelisted; set { _isSiteWhitelisted = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShieldIcon)); } }
        public string ShieldIcon => IsSiteWhitelisted ? "🟢" : "🛡️";

        public event Action<TabViewModel, string>? RequestNavigate;
        public event Action? RequestFocusAddressBar;
        public event Action<TabViewModel>? TabClosed;
        public event Action<TabViewModel>? TabPopOut;
        public event Action<TabViewModel>? TabScreenshot;
        public event Action? ClearCacheRequested;

        private readonly Stack<TabViewModel> _closedTabs = new Stack<TabViewModel>();

        public ICommand AddTabCommand { get; }
        public ICommand NavigateCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand UndoCloseTabCommand { get; }
        public ICommand PinTabCommand { get; }
        public ICommand DuplicateTabCommand { get; }
        public ICommand CloseOtherTabsCommand { get; }
        public ICommand CloseRightTabsCommand { get; }
        public ICommand PopOutCommand { get; }
        public ICommand ScreenshotCommand { get; }
        public ICommand ReloadCommand { get; set; } = new RelayCommand(_ => { });
        public ICommand ToggleShieldCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ToggleChartModeCommand { get; }

        public MainViewModel()
        {
            Tabs = new ObservableCollection<TabViewModel>();
            AddTabCommand = new RelayCommand(_ => AddTab());
            NavigateCommand = new RelayCommand(_ => ExecuteNavigate());
            CloseTabCommand = new RelayCommand(param => CloseTab(param as TabViewModel));
            UndoCloseTabCommand = new RelayCommand(_ => UndoCloseTab());
            PinTabCommand = new RelayCommand(param => { if (param is TabViewModel t) TogglePin(t); });
            DuplicateTabCommand = new RelayCommand(param => { if (param is TabViewModel t) DuplicateTab(t); });
            CloseOtherTabsCommand = new RelayCommand(param => { if (param is TabViewModel t) CloseOthers(t); });
            CloseRightTabsCommand = new RelayCommand(param => { if (param is TabViewModel t) CloseRight(t); });
            PopOutCommand = new RelayCommand(param => { if (param is TabViewModel t) TabPopOut?.Invoke(t); });
            ScreenshotCommand = new RelayCommand(param => { if (param is TabViewModel t) TabScreenshot?.Invoke(t); });
            ToggleShieldCommand = new RelayCommand(_ => ToggleShield());
            OpenSettingsCommand = new RelayCommand(_ => AddTab("settings://"));
            ToggleChartModeCommand = new RelayCommand(_ => IsChartMode = !IsChartMode);
        }

        private void ToggleShield()
        {
            if (SelectedTab != null) IsSiteWhitelisted = !IsSiteWhitelisted;
        }

        public void AddTab(string url = "homemarket://")
        {
            var newTab = new TabViewModel { Url = url };
            Tabs.Add(newTab);
            SelectedTab = newTab;
        }

        private void CloseTab(TabViewModel? tab)
        {
            if (tab == null) return;
            TabClosed?.Invoke(tab);
            _closedTabs.Push(tab); Tabs.Remove(tab);
            if (SelectedTab == tab) SelectedTab = Tabs.Count > 0 ? Tabs[Tabs.Count - 1] : null;
        }

        private void UndoCloseTab() { if (_closedTabs.Count > 0) { var tab = _closedTabs.Pop(); Tabs.Add(tab); SelectedTab = tab; } }
        private void TogglePin(TabViewModel tab) { tab.IsPinned = !tab.IsPinned; }
        private void DuplicateTab(TabViewModel tab) { AddTab(tab.Url); }
        private void CloseOthers(TabViewModel tab) { var others = Tabs.Where(t => t.Id != tab.Id).ToList(); foreach(var t in others) TabClosed?.Invoke(t); Tabs.Clear(); Tabs.Add(tab); SelectedTab = tab; }
        private void CloseRight(TabViewModel tab) { int index = Tabs.IndexOf(tab); var rightTabs = Tabs.Skip(index + 1).ToList(); foreach(var t in rightTabs) TabClosed?.Invoke(t); for(int i = Tabs.Count - 1; i > index; i--) Tabs.RemoveAt(i); }

        public void ExecuteNavigate()
        {
            if (SelectedTab == null || string.IsNullOrWhiteSpace(AddressBarText)) return;
            string input = AddressBarText.Trim();
            string url;
            if (input.StartsWith("tv ", StringComparison.OrdinalIgnoreCase)) url = $"https://www.tradingview.com/chart/?symbol={Uri.EscapeDataString(input.Substring(3).Trim())}";
            else if (input.Contains(".") && !input.Contains(" ")) url = input.StartsWith("http") ? input : "https://" + input;
            else url = $"https://www.google.com/search?q={Uri.EscapeDataString(input)}";
            RequestNavigate?.Invoke(SelectedTab, url);
        }

        public void FocusAddressBar() => RequestFocusAddressBar?.Invoke();
    }
}
