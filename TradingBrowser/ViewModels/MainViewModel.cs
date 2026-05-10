using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TradingBrowser.Views;

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
                if (_selectedTab != null) AddressBarText = _selectedTab.Url;
            }
        }

        private string _addressBarText = "";
        public string AddressBarText
        {
            get => _addressBarText;
            set { _addressBarText = value; OnPropertyChanged(); }
        }

        public event Action<TabViewModel, string>? RequestNavigate;
        public event Action? RequestFocusAddressBar;
        public event Action<TabViewModel>? TabClosed;
        public event Action<TabViewModel>? TabPopOut;
        public event Action<TabViewModel>? TabScreenshot;

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

        public MainViewModel()
        {
            Tabs = new ObservableCollection<TabViewModel>();
            AddTabCommand = new RelayCommand(_ => AddTab());
            NavigateCommand = new RelayCommand(_ => ExecuteNavigate());
            CloseTabCommand = new RelayCommand(param => CloseTab(param as TabViewModel));
            UndoCloseTabCommand = new RelayCommand(_ => UndoCloseTab());
            
            // Context Menu Commands
            PinTabCommand = new RelayCommand(param => TogglePin(param as TabViewModel));
            DuplicateTabCommand = new RelayCommand(param => DuplicateTab(param as TabViewModel));
            CloseOtherTabsCommand = new RelayCommand(param => CloseOthers(param as TabViewModel));
            CloseRightTabsCommand = new RelayCommand(param => CloseRight(param as TabViewModel));
            PopOutCommand = new RelayCommand(param => TabPopOut?.Invoke(param as TabViewModel));
            ScreenshotCommand = new RelayCommand(param => TabScreenshot?.Invoke(param as TabViewModel));
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
            _closedTabs.Push(tab); 
            Tabs.Remove(tab);
            if (SelectedTab == tab) SelectedTab = Tabs.Count > 0 ? Tabs[Tabs.Count - 1] : null;
        }

        private void UndoCloseTab()
        {
            if (_closedTabs.Count > 0)
            {
                var tab = _closedTabs.Pop();
                Tabs.Add(tab);
                SelectedTab = tab;
            }
        }

        private void TogglePin(TabViewModel? tab) { if(tab != null) tab.IsPinned = !tab.IsPinned; }
        
        private void DuplicateTab(TabViewModel? tab) { if(tab != null) AddTab(tab.Url); }

        private void CloseOthers(TabViewModel? tab)
        {
            if (tab == null) return;
            var others = Tabs.Where(t => t.Id != tab.Id).ToList();
            foreach(var t in others) TabClosed?.Invoke(t);
            Tabs.Clear();
            Tabs.Add(tab);
            SelectedTab = tab;
        }

        private void CloseRight(TabViewModel? tab)
        {
            if (tab == null) return;
            int index = Tabs.IndexOf(tab);
            var rightTabs = Tabs.Skip(index + 1).ToList();
            foreach(var t in rightTabs) TabClosed?.Invoke(t);
            for(int i = Tabs.Count - 1; i > index; i--) Tabs.RemoveAt(i);
        }

        public void ExecuteNavigate()
        {
            if (SelectedTab == null || string.IsNullOrWhiteSpace(AddressBarText)) return;
            string input = AddressBarText.Trim();
            string url;

            if (input.StartsWith("tv ", StringComparison.OrdinalIgnoreCase))
                url = $"https://www.tradingview.com/chart/?symbol={Uri.EscapeDataString(input.Substring(3).Trim())}";
            else if (input.Contains(".") && !input.Contains(" "))
                url = input.StartsWith("http") ? input : "https://" + input;
            else
                url = $"https://www.google.com/search?q={Uri.EscapeDataString(input)}";

            RequestNavigate?.Invoke(SelectedTab, url);
        }

        public void FocusAddressBar() => RequestFocusAddressBar?.Invoke();
    }
}
