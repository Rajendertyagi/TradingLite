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
            set { _selectedTab = value; OnPropertyChanged(); }
        }

        public ICommand AddTabCommand { get; }
        public ICommand CloseTabCommand { get; }

        public MainViewModel()
        {
            Tabs = new ObservableCollection<TabViewModel>();
            AddTabCommand = new RelayCommand(_ => AddTab());
            CloseTabCommand = new RelayCommand(param => CloseTab(param as TabViewModel));

            // Create initial tab on startup
            AddTab();
        }

        private void AddTab()
        {
            var newTab = new TabViewModel();
            Tabs.Add(newTab);
            SelectedTab = newTab; // Auto-select new tab
        }

        private void CloseTab(TabViewModel? tab)
        {
            if (tab == null) return;
            Tabs.Remove(tab);

            if (SelectedTab == tab)
            {
                SelectedTab = Tabs.Count > 0 ? Tabs[Tabs.Count - 1] : null;
            }
        }
    }
}
