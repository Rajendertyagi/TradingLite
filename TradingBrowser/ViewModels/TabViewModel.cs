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

        private readonly Stack<TabViewModel> _closedTabs = new Stack<TabViewModel>();

        public ICommand AddTabCommand { get; }
        public ICommand NavigateCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand UndoCloseTabCommand { get; }

        public MainViewModel()
        {
            Tabs = new ObservableCollection<TabViewModel>();
            AddTabCommand = new RelayCommand(_ => AddTab());
            NavigateCommand = new RelayCommand(_ => ExecuteNavigate());
            CloseTabCommand = new RelayCommand(param => CloseTab(param as TabViewModel));
            UndoCloseTabCommand = new RelayCommand(_ => UndoCloseTab());
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
            _closedTabs.Push(tab); // Save for Ctrl+Shift+T
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

        public void ExecuteNavigate()
        {
            if (SelectedTab == null || string.IsNullOrWhiteSpace(AddressBarText)) return;
            string input = AddressBarText.Trim();
            string url;

            if (input.StartsWith("tv ", System.StringComparison.OrdinalIgnoreCase))
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
