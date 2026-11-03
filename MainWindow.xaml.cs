using GasAgencyManager.Data;
using GasAgencyManager.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace GasAgencyManager
{
    public partial class MainWindow : Window
    {
        private int _morningLoad = 0;
        private DatabaseService _dbService = new DatabaseService();
        private List<ProductEntry> _plantProductEntries = new();
        private List<ProductEntry> _morningProductEntries = new();
        private List<ProductEntry> _eveningProductEntries = new();
        private List<StockItem> _masterStockRows = new();
        private DateTime _businessDate = DateTime.Today;
        private readonly DispatcherTimer _systemDateTimer = new();
        private List<Driver> _allDrivers = new();
        private List<Vehicle> _allVehicles = new();
        private HashSet<(int DriverId, int VehicleId)> _settledPairs = new();
        private HashSet<(int DriverId, int VehicleId)> _morningOutPairs = new();
        private HashSet<(int DriverId, int VehicleId)> _accountingPendingPairs = new();
        private List<AccountingSettlementRecord> _accountingHistory = new();
        private int _expectedRefillCount;
        private bool _refreshingCombos;

        // Track morning uplift per product type (for evening reconciliation)
        private Dictionary<string, int> _morningUpliftByProduct = new();

        // Shortages pending reason selection (evening reconciliation)
        private Dictionary<string, int> _pendingShortages = new();

        private const string Commercial19kgProduct = "19kg Commercial";

        private string[] AllProducts =
        {
            "14.2kg Domestic",
            "19kg Commercial",
            "5kg Domestic",
            "10kg Domestic",
            "5kg FTL",
            "2kg FTL"
        };

        private string[] HotelList =
        {
            "CUSTOMER F",
            "CUSTOMER G",
            "CUSTOMER H",
            "CUSTOMER C",
            "CUSTOMER D",
            "CUSTOMER E",
            "CUSTOMER I",
            "CUSTOMER J",
            "CUSTOMER K",
            "CUSTOMER A",
            "CUSTOMER B",
            "OTHER"
        };

        // Accounting tab state
        private AccountingEntry _accountingEntry = new();
        private readonly string _accountingStateDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GasAgencyManager");
        private readonly string _accountingStateFileName = "accounting-latest.json";

        private void LoadLogisticsData()
        {
            _allDrivers = _dbService.GetDrivers();
            _allVehicles = _dbService.GetVehicles();

            RefreshSettledPairs();
            RefreshMorningOutPairs();
            RefreshMorningCombos();
            RefreshEveningCombos();
            RefreshAccountingCombos();
        }

        private void RefreshSettledPairs()
        {
            _settledPairs.Clear();
            if (!TryGetSelectedWarehouseId(out int warehouseId))
                return;

            var completed = _dbService.GetCompletedSettlementsForDate(warehouseId, CurrentBusinessDateString);
            foreach (var settlement in completed)
                _settledPairs.Add((settlement.DriverId, settlement.VehicleId));
        }

        private void RefreshMorningCombos()
        {
            int? selectedDriverId = MorningDriverCombo.SelectedValue as int?;
            int? selectedVehicleId = MorningVehicleCombo.SelectedValue as int?;

            _refreshingCombos = true;
            try
            {
                MorningDriverCombo.ItemsSource = _allDrivers.Select(d => new DriverComboItem
                {
                    Id = d.Id,
                    Name = d.Name,
                    IsSettled = selectedVehicleId.HasValue && _settledPairs.Contains((d.Id, selectedVehicleId.Value))
                }).ToList();

                MorningVehicleCombo.ItemsSource = _allVehicles.Select(v => new VehicleComboItem
                {
                    Id = v.Id,
                    VehicleNumber = v.VehicleNumber,
                    IsSettled = selectedDriverId.HasValue && _settledPairs.Contains((selectedDriverId.Value, v.Id))
                }).ToList();

                if (selectedDriverId.HasValue)
                    MorningDriverCombo.SelectedValue = selectedDriverId.Value;
                if (selectedVehicleId.HasValue)
                    MorningVehicleCombo.SelectedValue = selectedVehicleId.Value;
            }
            finally
            {
                _refreshingCombos = false;
            }
        }

        private static bool TryGetComboIntValue(object? value, out int id)
        {
            id = 0;
            switch (value)
            {
                case int i:
                    id = i;
                    return true;
                case long l:
                    id = (int)l;
                    return true;
                case string s when int.TryParse(s, out int parsed):
                    id = parsed;
                    return true;
                default:
                    return false;
            }
        }

        private bool TryGetSelectedWarehouseId(out int warehouseId)
        {
            warehouseId = 0;
            if (TryGetComboIntValue(WarehouseCombo.SelectedValue, out warehouseId))
                return true;

            if (WarehouseCombo.SelectedItem is Warehouse warehouse)
            {
                warehouseId = warehouse.Id;
                return true;
            }

            return false;
        }

        private void RefreshMorningOutPairs(int? warehouseIdOverride = null)
        {
            _morningOutPairs.Clear();

            int warehouseId;
            if (warehouseIdOverride.HasValue)
                warehouseId = warehouseIdOverride.Value;
            else if (!TryGetSelectedWarehouseId(out warehouseId))
                return;

            foreach (var pair in _dbService.GetMorningOutPairsForDate(warehouseId, CurrentBusinessDateString))
                _morningOutPairs.Add(pair);
        }

        private bool HasMorningOutForPair(int driverId, int vehicleId) =>
            _morningOutPairs.Contains((driverId, vehicleId));

        private void RefreshEveningCombos(bool refreshDriverList = true, bool refreshVehicleList = true)
        {
            int? selectedDriverId = GetComboSelectedId(EveningDriverCombo);
            int? selectedVehicleId = GetComboSelectedId(EveningVehicleCombo);

            var eligiblePairs = _morningOutPairs
                .Where(p => !_settledPairs.Contains(p))
                .ToList();

            _refreshingCombos = true;
            try
            {
                if (refreshDriverList)
                {
                    var availableDriverIds = eligiblePairs
                        .Where(p => !selectedVehicleId.HasValue || p.VehicleId == selectedVehicleId.Value)
                        .Select(p => p.DriverId)
                        .Distinct()
                        .ToHashSet();

                    EveningDriverCombo.ItemsSource = _allDrivers
                        .Where(d => availableDriverIds.Contains(d.Id))
                        .Select(d => new DriverComboItem
                        {
                            Id = d.Id,
                            Name = d.Name,
                            IsSettled = selectedVehicleId.HasValue && _settledPairs.Contains((d.Id, selectedVehicleId.Value))
                        }).ToList();

                    RestoreComboSelection(EveningDriverCombo, selectedDriverId, availableDriverIds);
                }

                if (refreshVehicleList)
                {
                    selectedDriverId = GetComboSelectedId(EveningDriverCombo);

                    var availableVehicleIds = eligiblePairs
                        .Where(p => !selectedDriverId.HasValue || p.DriverId == selectedDriverId.Value)
                        .Select(p => p.VehicleId)
                        .Distinct()
                        .ToHashSet();

                    EveningVehicleCombo.ItemsSource = _allVehicles
                        .Where(v => availableVehicleIds.Contains(v.Id))
                        .Select(v => new VehicleComboItem
                        {
                            Id = v.Id,
                            VehicleNumber = v.VehicleNumber,
                            IsSettled = selectedDriverId.HasValue && _settledPairs.Contains((selectedDriverId.Value, v.Id))
                        }).ToList();

                    RestoreComboSelection(EveningVehicleCombo, selectedVehicleId, availableVehicleIds);
                }

                selectedDriverId = GetComboSelectedId(EveningDriverCombo);
                selectedVehicleId = GetComboSelectedId(EveningVehicleCombo);
                if (selectedDriverId.HasValue && selectedVehicleId.HasValue &&
                    !eligiblePairs.Contains((selectedDriverId.Value, selectedVehicleId.Value)))
                {
                    EveningVehicleCombo.SelectedIndex = -1;
                }
            }
            finally
            {
                _refreshingCombos = false;
            }
        }

        private void RefreshAccountingPendingPairs()
        {
            _accountingPendingPairs.Clear();
            if (!TryGetSelectedWarehouseId(out int warehouseId))
                return;

            foreach (var trip in _dbService.GetAccountingPendingTrips(warehouseId, CurrentBusinessDateString))
                _accountingPendingPairs.Add((trip.DriverId, trip.VehicleId));
        }

        private void RefreshAccountingCombos(bool refreshDriverList = true, bool refreshVehicleList = true)
        {
            int? selectedDriverId = GetComboSelectedId(AcctDriverCombo);
            int? selectedVehicleId = GetComboSelectedId(AcctVehicleCombo);

            RefreshAccountingPendingPairs();
            var eligiblePairs = _accountingPendingPairs.ToList();

            _refreshingCombos = true;
            try
            {
                if (refreshDriverList)
                {
                    var availableDriverIds = eligiblePairs
                        .Where(p => !selectedVehicleId.HasValue || p.VehicleId == selectedVehicleId.Value)
                        .Select(p => p.DriverId)
                        .Distinct()
                        .ToHashSet();

                    AcctDriverCombo.ItemsSource = _allDrivers
                        .Where(d => availableDriverIds.Contains(d.Id))
                        .Select(d => new DriverComboItem { Id = d.Id, Name = d.Name })
                        .ToList();

                    RestoreComboSelection(AcctDriverCombo, selectedDriverId, availableDriverIds);
                }

                if (refreshVehicleList)
                {
                    selectedDriverId = GetComboSelectedId(AcctDriverCombo);

                    var availableVehicleIds = eligiblePairs
                        .Where(p => !selectedDriverId.HasValue || p.DriverId == selectedDriverId.Value)
                        .Select(p => p.VehicleId)
                        .Distinct()
                        .ToHashSet();

                    AcctVehicleCombo.ItemsSource = _allVehicles
                        .Where(v => availableVehicleIds.Contains(v.Id))
                        .Select(v => new VehicleComboItem { Id = v.Id, VehicleNumber = v.VehicleNumber })
                        .ToList();

                    RestoreComboSelection(AcctVehicleCombo, selectedVehicleId, availableVehicleIds);
                }

                selectedDriverId = GetComboSelectedId(AcctDriverCombo);
                selectedVehicleId = GetComboSelectedId(AcctVehicleCombo);
                if (selectedDriverId.HasValue && selectedVehicleId.HasValue &&
                    !eligiblePairs.Contains((selectedDriverId.Value, selectedVehicleId.Value)))
                {
                    AcctVehicleCombo.SelectedIndex = -1;
                }
            }
            finally
            {
                _refreshingCombos = false;
            }
        }

        private void RefreshAccountingFromDatabase()
        {
            RefreshAccountingPendingPairs();
            RefreshAccountingCombos();
            UpdateAccountingTabState();
        }

        private static int? GetComboSelectedId(ComboBox combo)
        {
            if (TryGetComboIntValue(combo.SelectedValue, out int id))
                return id;

            return combo.SelectedItem switch
            {
                DriverComboItem d => d.Id,
                VehicleComboItem v => v.Id,
                Driver dr => dr.Id,
                Vehicle ve => ve.Id,
                _ => null
            };
        }

        private static void RestoreComboSelection(ComboBox combo, int? selectedId, HashSet<int> availableIds)
        {
            if (selectedId.HasValue && availableIds.Contains(selectedId.Value))
                combo.SelectedValue = selectedId.Value;
            else
                combo.SelectedIndex = -1;
        }

        // Call LoadLogisticsData(); inside your public MainWindow() constructor!

        public MainWindow()
        {
            InitializeComponent();

            _dbService = new DatabaseService();

            if (App.CurrentUser != null)
            {
                string roleLabel = App.CurrentUser.Role == UserRole.Owner ? "Owner" : "Manager";
                CurrentUserText.Text = $"User: {App.CurrentUser.Username} ({roleLabel})";
            }

            InitializeProductEntries();
            LoadLogisticsData();
            LoadBusinessDate();
            AcctDatePicker.SelectedDate = _businessDate;
            AcctHistoryFromDate.SelectedDate = _businessDate;
            AcctHistoryToDate.SelectedDate = _businessDate;
            UpdateDateDisplays();
            StartSystemDateTimer();
            LoadAccountingSnapshot();

            WarehouseCombo.SelectionChanged += WarehouseCombo_SelectionChanged;
            MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;
            Closing += MainWindow_Closing;

            LoadWarehouseData();

            ApplyRolePermissions();
            ArrangeTabs();
            LoadAdminLists();
            ValidateWarehousesOnStartup();
            ApplyWarehouseGate();

            LoadCommercialLedger();
            LedgerEntryTypeCombo.SelectedIndex = 0;
            LedgerColumnGuideText.Text = "Columns: " + string.Join(" | ", CommercialCustomerImporter.RegisterColumnGuide);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveAccountingSnapshot(createBackup: true);
        }

        private Dictionary<string, TextBox> GetAccountingTextBoxes()
        {
            return new Dictionary<string, TextBox>
            {
                ["MiscSbcToDbcPriceInput"] = MiscSbcToDbcPriceInput,
                ["MiscSbcToDbcQtyInput"] = MiscSbcToDbcQtyInput,
                ["MiscSbcToDbcOnlineQtyInput"] = MiscSbcToDbcOnlineQtyInput,
                ["MiscSbcToDbcGPayQtyInput"] = MiscSbcToDbcGPayQtyInput,
                ["MiscNewSbcPriceInput"] = MiscNewSbcPriceInput,
                ["MiscNewSbcQtyInput"] = MiscNewSbcQtyInput,
                ["MiscNewSbcOnlineQtyInput"] = MiscNewSbcOnlineQtyInput,
                ["MiscNewSbcGPayQtyInput"] = MiscNewSbcGPayQtyInput,
                ["MiscNewDbcPriceInput"] = MiscNewDbcPriceInput,
                ["MiscNewDbcQtyInput"] = MiscNewDbcQtyInput,
                ["MiscNewDbcOnlineQtyInput"] = MiscNewDbcOnlineQtyInput,
                ["MiscNewDbcGPayQtyInput"] = MiscNewDbcGPayQtyInput,
                ["MiscOtherPriceInput"] = MiscOtherPriceInput,
                ["MiscOtherQtyInput"] = MiscOtherQtyInput,
                ["MiscOtherOnlineQtyInput"] = MiscOtherOnlineQtyInput,
                ["MiscOtherGPayQtyInput"] = MiscOtherGPayQtyInput,
                ["Refill1PriceInput"] = Refill1PriceInput,
                ["Refill1QtyInput"] = Refill1QtyInput,
                ["Refill1OnlineQtyInput"] = Refill1OnlineQtyInput,
                ["Refill1GPayQtyInput"] = Refill1GPayQtyInput,
                ["Refill2PriceInput"] = Refill2PriceInput,
                ["Refill2QtyInput"] = Refill2QtyInput,
                ["Refill2OnlineQtyInput"] = Refill2OnlineQtyInput,
                ["Refill2GPayQtyInput"] = Refill2GPayQtyInput,
                ["Refill3PriceInput"] = Refill3PriceInput,
                ["Refill3QtyInput"] = Refill3QtyInput,
                ["Refill3OnlineQtyInput"] = Refill3OnlineQtyInput,
                ["Refill3GPayQtyInput"] = Refill3GPayQtyInput,
                ["ChangeProvidedInput"] = ChangeProvidedInput,
                ["FuelExpenseInput"] = FuelExpenseInput,
                ["OtherExpenseInput"] = OtherExpenseInput,
                ["Notes5Input"] = Notes5Input,
                ["Notes10Input"] = Notes10Input,
                ["Notes20Input"] = Notes20Input,
                ["Notes50Input"] = Notes50Input,
                ["Notes100Input"] = Notes100Input,
                ["Notes200Input"] = Notes200Input,
                ["Notes500Input"] = Notes500Input
            };
        }

        private string GetAccountingStateFilePath()
        {
            return Path.Combine(_accountingStateDir, _accountingStateFileName);
        }

        private void SaveAccountingSnapshot(bool createBackup)
        {
            try
            {
                Directory.CreateDirectory(_accountingStateDir);
                var snapshot = new AccountingTabSnapshot
                {
                    SavedAt = DateTime.Now,
                    Date = AcctDatePicker.SelectedDate,
                    DriverId = AcctDriverCombo.SelectedValue as int?,
                    VehicleId = AcctVehicleCombo.SelectedValue as int?
                };

                foreach (var kvp in GetAccountingTextBoxes())
                {
                    snapshot.Fields[kvp.Key] = kvp.Value.Text ?? string.Empty;
                }

                var latestPath = GetAccountingStateFilePath();
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(latestPath, json);

                if (createBackup)
                {
                    var backupName = $"accounting-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json";
                    var backupPath = Path.Combine(_accountingStateDir, backupName);
                    File.Copy(latestPath, backupPath, overwrite: true);
                }
            }
            catch
            {
                // Ignore persistence errors to avoid blocking shutdown.
            }
        }

        private void LoadAccountingSnapshot()
        {
            try
            {
                var latestPath = GetAccountingStateFilePath();
                if (!File.Exists(latestPath))
                {
                    return;
                }

                var json = File.ReadAllText(latestPath);
                var snapshot = JsonSerializer.Deserialize<AccountingTabSnapshot>(json);
                if (snapshot == null)
                {
                    return;
                }

                AcctDatePicker.SelectedDate = snapshot.Date ?? _businessDate;
                bool restoredTrip = false;
                if (snapshot.DriverId.HasValue && snapshot.VehicleId.HasValue
                    && _accountingPendingPairs.Contains((snapshot.DriverId.Value, snapshot.VehicleId.Value)))
                {
                    AcctDriverCombo.SelectedValue = snapshot.DriverId.Value;
                    AcctVehicleCombo.SelectedValue = snapshot.VehicleId.Value;
                    restoredTrip = true;
                }

                if (restoredTrip)
                {
                    var textBoxes = GetAccountingTextBoxes();
                    foreach (var kvp in snapshot.Fields)
                    {
                        if (textBoxes.TryGetValue(kvp.Key, out var textBox))
                            textBox.Text = kvp.Value ?? string.Empty;
                    }

                    MiscSalesInputs_TextChanged(this, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
                    RefillTierInputs_TextChanged(this, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
                    CalculateCashBtn_Click(this, new RoutedEventArgs());
                    PopulateAccountingSummary();
                }
            }
            catch
            {
                // Ignore invalid/corrupt snapshot file and keep app usable.
            }
        }

        private bool IsOwner() => App.CurrentUser?.Role == UserRole.Owner;

        private bool RequireOwner(string actionDescription)
        {
            if (IsOwner()) return true;
            MessageBox.Show($"Access denied. Only the Owner can {actionDescription}.", "Permission Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void ApplyRolePermissions()
        {
            bool isOwner = IsOwner();

            AdminTab.Visibility = isOwner ? Visibility.Visible : Visibility.Collapsed;
            MasterStockTab.Visibility = isOwner ? Visibility.Visible : Visibility.Collapsed;
            CommercialLedgerTab.Visibility = isOwner ? Visibility.Visible : Visibility.Collapsed;
            PlantReversalPanel.Visibility = isOwner ? Visibility.Visible : Visibility.Collapsed;

            if (isOwner)
                LoadPlantPurchaseHistory();
        }

        private void LoadPlantPurchaseHistory()
        {
            if (!IsOwner() || !TryGetSelectedWarehouseId(out int warehouseId))
            {
                PlantPurchaseHistoryGrid.ItemsSource = null;
                return;
            }

            PlantPurchaseHistoryGrid.ItemsSource = _dbService.GetIoclPlantPurchases(warehouseId);
        }

        private void RefreshPlantPurchaseHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOwner("reverse IOCL plant purchases")) return;
            LoadPlantPurchaseHistory();
        }

        private void ReversePlantPurchaseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOwner("reverse IOCL plant purchases")) return;

            if (PlantPurchaseHistoryGrid.SelectedItem is not TransactionLog selected)
            {
                MessageBox.Show("Please select an IOCL plant purchase entry to reverse.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string reason = (PlantReversalReasonCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Data Entry Mistake";
            string ownerName = App.CurrentUser?.Username ?? "owner";

            var confirm = MessageBox.Show(
                $"Reverse this IOCL entry?\n\n{selected.Timestamp}\n{selected.CylinderType}\nFilled change: {selected.FilledChange}\nEmpty change: {selected.EmptyChange}\n\nReason: {reason}\n\nStock will be adjusted back automatically.",
                "Confirm Reversal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            if (!_dbService.TryReverseIoclPlantPurchase(selected.Id, reason, ownerName, out string error))
            {
                MessageBox.Show(error, "Reversal Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show("IOCL plant purchase entry reversed successfully.", "Reversed", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshStockGrid();
            LoadPlantPurchaseHistory();
        }

        private void ArrangeTabs()
        {
            var tabs = MainTabControl.Items.Cast<TabItem>().ToList();
            MainTabControl.Items.Clear();

            TabItem? Pick(string name) => tabs.FirstOrDefault(t => t.Name == name);

            string[] order = IsOwner()
                ? new[] { "AdminTab", "MasterStockTab", "IoclPlantTab", "MorningOutTab", "EveningInTab", "AccountingTab", "StockReportTab", "CommercialLedgerTab" }
                : new[] { "IoclPlantTab", "MorningOutTab", "EveningInTab", "AccountingTab", "StockReportTab" };

            foreach (var tabName in order)
            {
                if (Pick(tabName) is TabItem tab)
                    MainTabControl.Items.Add(tab);
            }
        }

        private void ApplyWarehouseGate()
        {
            bool hasWarehouse = _dbService.GetWarehouses().Count > 0;
            bool isOwner = IsOwner();

            foreach (TabItem tab in MainTabControl.Items)
            {
                if (tab.Visibility == Visibility.Collapsed)
                    continue;

                bool isAdminTab = tab == AdminTab;
                bool isEveningTab = tab == EveningInTab;
                bool isAccountingTab = tab == AccountingTab;
                bool hasMorningOut = _morningOutPairs.Count > 0;
                bool hasEveningSettlement = TryGetSelectedWarehouseId(out int warehouseId)
                    && _dbService.HasAnyEveningSettlementForDate(warehouseId, CurrentBusinessDateString);
                bool hasAccountingHistory = _dbService.HasAnyAccountingSettlement();

                if (isEveningTab)
                    tab.IsEnabled = hasWarehouse && hasMorningOut;
                else if (isAccountingTab)
                    tab.IsEnabled = hasWarehouse && (hasEveningSettlement || hasAccountingHistory);
                else
                    tab.IsEnabled = hasWarehouse || (isOwner && isAdminTab);
            }

            WarehouseCombo.IsEnabled = hasWarehouse;
            DayEndButton.IsEnabled = hasWarehouse;

            UpdateEveningInTabState();
            UpdateAccountingTabState();

            if (!hasWarehouse && isOwner)
                MainTabControl.SelectedItem = AdminTab;
        }

        private void UpdateEveningInTabState()
        {
            bool hasMorningOut = _morningOutPairs.Count > 0;
            NoMorningOutBanner.Visibility = hasMorningOut ? Visibility.Collapsed : Visibility.Visible;

            if (hasMorningOut)
            {
                EveningSelectionPanel.IsEnabled = true;
                EveningDriverCombo.IsEnabled = true;
                EveningVehicleCombo.IsEnabled = true;
            }
            else
            {
                NoMorningOutBannerText.Text =
                    "Evening In is disabled until at least one Morning Out entry is saved for today.";
                NoMorningOutSelectionBanner.Visibility = Visibility.Collapsed;
                MorningUpliftSummaryPanel.Visibility = Visibility.Collapsed;
                EveningSelectionPanel.IsEnabled = false;
                EveningReturnsPanel.IsEnabled = false;
                ReturnButton.IsEnabled = false;
                ClearEveningMorningQtyHints();
            }
        }

        private void RefreshEveningInFromDatabase(int? warehouseIdOverride = null)
        {
            RefreshSettledPairs();
            RefreshMorningOutPairs(warehouseIdOverride);
            RefreshEveningCombos();
            ApplyWarehouseGate();
            UpdateAccountingSettlementState();
            RefreshAccountingFromDatabase();
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Contains(EveningInTab))
                RefreshEveningInFromDatabase();
            if (e.AddedItems.Contains(AccountingTab))
            {
                RefreshAccountingFromDatabase();
                LoadAccountingHistoryForCurrentRange();
            }
        }

        private void ValidateWarehousesOnStartup()
        {
            if (_dbService.GetWarehouses().Count > 0)
                return;

            ApplyWarehouseGate();

            if (IsOwner())
            {
                MessageBox.Show(
                    "No warehouse configured yet.\n\nAs Owner, go to the Admin tab and add at least one warehouse. All other tabs will remain disabled until a warehouse is added.",
                    "Warehouse Setup Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "No warehouse configured yet.\n\nPlease ask the Owner to log in and add a warehouse in the Admin tab before you can begin daily operations.",
                    "Warehouse Setup Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DisableMainTabs() => ApplyWarehouseGate();

        private void EnableMainTabs()
        {
            ApplyWarehouseGate();
        }

        private void LoadBusinessDate()
        {
            _businessDate = _dbService.GetBusinessDate();
        }

        private void UpdateDateDisplays()
        {
            BusinessDateText.Text = _businessDate.ToString("dddd, dd MMM yyyy");
            SystemDateText.Text = DateTime.Now.ToString("dddd, dd MMM yyyy HH:mm");
            StatusText.Text = $"Working on {_businessDate:dd MMM yyyy} | System: {DateTime.Now:dd MMM yyyy HH:mm}";
        }

        private void StartSystemDateTimer()
        {
            _systemDateTimer.Interval = TimeSpan.FromMinutes(1);
            _systemDateTimer.Tick += (_, _) => UpdateDateDisplays();
            _systemDateTimer.Start();
        }

        private string CurrentBusinessDateString => _businessDate.ToString("yyyy-MM-dd");

        private bool TryGetSelectedDriverVehicle(ComboBox driverCombo, ComboBox vehicleCombo,
            out int driverId, out int vehicleId, out string driverName, out string vehicleNumber)
        {
            driverId = 0;
            vehicleId = 0;
            driverName = (driverCombo.SelectedItem as DriverComboItem)?.Name
                ?? (driverCombo.SelectedItem as Driver)?.Name ?? "";
            vehicleNumber = (vehicleCombo.SelectedItem as VehicleComboItem)?.VehicleNumber
                ?? (vehicleCombo.SelectedItem as Vehicle)?.VehicleNumber ?? "";

            if (driverCombo.SelectedValue is int dId)
                driverId = dId;
            else if (TryGetComboIntValue(driverCombo.SelectedValue, out int parsedDriverId))
                driverId = parsedDriverId;
            else if (driverCombo.SelectedItem is DriverComboItem driverItem)
                driverId = driverItem.Id;
            else if (driverCombo.SelectedItem is Driver driver)
                driverId = driver.Id;

            if (vehicleCombo.SelectedValue is int vId)
                vehicleId = vId;
            else if (TryGetComboIntValue(vehicleCombo.SelectedValue, out int parsedVehicleId))
                vehicleId = parsedVehicleId;
            else if (vehicleCombo.SelectedItem is VehicleComboItem vehicleItem)
                vehicleId = vehicleItem.Id;
            else if (vehicleCombo.SelectedItem is Vehicle vehicle)
                vehicleId = vehicle.Id;

            return driverId > 0 && vehicleId > 0;
        }

        private bool IsCurrentSelectionSettled(ComboBox driverCombo, ComboBox vehicleCombo)
        {
            if (!TryGetSelectedWarehouseId(out int warehouseId))
                return false;

            if (!TryGetSelectedDriverVehicle(driverCombo, vehicleCombo,
                out int driverId, out int vehicleId, out _, out _))
                return false;

            return _dbService.IsSettlementCompleted(warehouseId, driverId, vehicleId, CurrentBusinessDateString);
        }

        private void MorningDriverVehicle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshingCombos) return;
            RefreshMorningCombos();
            UpdateMorningOperationState();
        }

        private void EveningDriverVehicle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshingCombos) return;

            try
            {
                if (sender == EveningDriverCombo)
                    RefreshEveningCombos(refreshDriverList: false, refreshVehicleList: true);
                else if (sender == EveningVehicleCombo)
                    RefreshEveningCombos(refreshDriverList: true, refreshVehicleList: false);
                else
                    RefreshEveningCombos();

                LoadMorningUpliftForEveningSelection();
                UpdateMorningUpliftSummaryDisplay();
                UpdateAccountingSettlementState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error updating Evening In: {ex.Message}",
                    "Evening In Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadMorningUpliftForEveningSelection()
        {
            _morningUpliftByProduct.Clear();

            if (!TryGetSelectedWarehouseId(out int warehouseId))
                return;

            if (!TryGetSelectedDriverVehicle(EveningDriverCombo, EveningVehicleCombo,
                out int driverId, out int vehicleId, out _, out _))
                return;

            if (!HasMorningOutForPair(driverId, vehicleId))
                return;

            var uplifts = _dbService.GetMorningUplifts(warehouseId, driverId, vehicleId, CurrentBusinessDateString);
            foreach (var uplift in uplifts)
                _morningUpliftByProduct[uplift.Key] = uplift.Value;
        }

        private void ClearEveningMorningQtyHints()
        {
            foreach (var product in _eveningProductEntries)
                product.MorningUpliftQty = 0;
        }

        private void UpdateMorningUpliftSummaryDisplay()
        {
            if (_morningOutPairs.Count == 0)
            {
                MorningUpliftSummaryPanel.Visibility = Visibility.Collapsed;
                NoMorningOutSelectionBanner.Visibility = Visibility.Collapsed;
                ClearEveningMorningQtyHints();
                return;
            }

            if (!TryGetSelectedDriverVehicle(EveningDriverCombo, EveningVehicleCombo,
                out int driverId, out int vehicleId, out string driverName, out string vehicleNumber))
            {
                MorningUpliftSummaryPanel.Visibility = Visibility.Collapsed;
                NoMorningOutSelectionBanner.Visibility = Visibility.Visible;
                ClearEveningMorningQtyHints();
                return;
            }

            if (!HasMorningOutForPair(driverId, vehicleId) || _morningUpliftByProduct.Count == 0)
            {
                MorningUpliftSummaryPanel.Visibility = Visibility.Collapsed;
                NoMorningOutSelectionBanner.Visibility = Visibility.Visible;
                ClearEveningMorningQtyHints();
                return;
            }

            NoMorningOutSelectionBanner.Visibility = Visibility.Collapsed;
            int total = _morningUpliftByProduct.Values.Sum();
            var lines = _morningUpliftByProduct
                .OrderBy(x => x.Key)
                .Select(x => $"  • {x.Key}: {x.Value}");
            MorningUpliftSummaryText.Text =
                $"{driverName} / {vehicleNumber} — Total sent out: {total} cylinder(s)\n" +
                string.Join("\n", lines);
            MorningUpliftSummaryPanel.Visibility = Visibility.Visible;

            foreach (var product in _eveningProductEntries)
            {
                product.MorningUpliftQty = _morningUpliftByProduct.TryGetValue(product.ProductName, out int qty) ? qty : 0;
            }
        }

        private void UpdateMorningOperationState()
        {
            bool isSettled = IsCurrentSelectionSettled(MorningDriverCombo, MorningVehicleCombo);
            SaveButton.IsEnabled = WarehouseCombo.SelectedValue != null && !isSettled;

            if (isSettled && TryGetSelectedDriverVehicle(MorningDriverCombo, MorningVehicleCombo,
                out _, out _, out string driverName, out string vehicleNumber))
            {
                StatusText.Text = $"Accounting already completed for {driverName} / {vehicleNumber} on {_businessDate:dd MMM yyyy}.";
            }
            else
            {
                UpdateDateDisplays();
            }
        }

        private void UpdateAccountingSettlementState()
        {
            if (_morningOutPairs.Count == 0)
            {
                UpdateEveningInTabState();
                return;
            }

            bool hasWarehouse = WarehouseCombo.SelectedValue != null;
            bool hasSelection = TryGetSelectedDriverVehicle(EveningDriverCombo, EveningVehicleCombo,
                out int driverId, out int vehicleId, out _, out _);
            bool hasMorningOut = hasSelection && HasMorningOutForPair(driverId, vehicleId) && _morningUpliftByProduct.Count > 0;
            bool isSettled = IsCurrentSelectionSettled(EveningDriverCombo, EveningVehicleCombo);

            SettlementCompletedBanner.Visibility = Visibility.Collapsed;

            bool canSelectTrips = _morningOutPairs.Count > 0;
            EveningSelectionPanel.IsEnabled = canSelectTrips;
            EveningDriverCombo.IsEnabled = canSelectTrips;
            EveningVehicleCombo.IsEnabled = canSelectTrips;

            EveningReturnsPanel.IsEnabled = hasMorningOut && !isSettled;
            ReturnButton.IsEnabled = hasWarehouse && hasMorningOut && !isSettled;

            if (isSettled && TryGetSelectedDriverVehicle(EveningDriverCombo, EveningVehicleCombo,
                out _, out _, out string driverName, out string vehicleNumber))
            {
                SettlementCompletedBanner.Visibility = Visibility.Visible;
                SettlementCompletedText.Text =
                    $"Accounting and settlement already completed for {driverName} / {vehicleNumber} on {_businessDate:dddd, dd MMM yyyy}.";
                EveningReturnsPanel.IsEnabled = false;
                ReturnButton.IsEnabled = false;
            }

            UpdateMorningUpliftSummaryDisplay();
        }

        private void UpdateAccountingTabState()
        {
            bool hasWarehouse = TryGetSelectedWarehouseId(out int warehouseId);
            bool hasEveningSettlement = hasWarehouse
                && _dbService.HasAnyEveningSettlementForDate(warehouseId, CurrentBusinessDateString);
            bool hasPending = _accountingPendingPairs.Count > 0;
            bool hasSelection = TryGetSelectedDriverVehicle(AcctDriverCombo, AcctVehicleCombo,
                out int driverId, out int vehicleId, out _, out _)
                && _accountingPendingPairs.Contains((driverId, vehicleId));

            NoEveningForAccountingBanner.Visibility = hasWarehouse && !hasEveningSettlement
                ? Visibility.Visible : Visibility.Collapsed;
            NoAccountingPendingBanner.Visibility = hasEveningSettlement && !hasPending
                ? Visibility.Visible : Visibility.Collapsed;
            AcctSelectTripBanner.Visibility = hasEveningSettlement && hasPending && !hasSelection
                ? Visibility.Visible : Visibility.Collapsed;

            if (hasEveningSettlement && !hasPending)
            {
                NoAccountingPendingBannerText.Text =
                    $"All driver/vehicle trips have been accounted for on {_businessDate:dddd, dd MMM yyyy}.";
            }

            bool canSelect = hasEveningSettlement && hasPending;
            AcctSelectionPanel.IsEnabled = canSelect;
            AcctDriverCombo.IsEnabled = canSelect;
            AcctVehicleCombo.IsEnabled = canSelect;
            AcctFormPanel.IsEnabled = hasSelection;
            SaveAcctSummaryBtn.IsEnabled = hasSelection;
            AcctHistoryPanel.IsEnabled = hasWarehouse;
        }

        private void ResetAccountingTabForm()
        {
            _expectedRefillCount = 0;
            AcctDriverCombo.SelectedIndex = -1;
            AcctVehicleCombo.SelectedIndex = -1;

            foreach (var textBox in GetAccountingTextBoxes().Values)
                textBox.Clear();

            ClearAccountingSummaryDisplay();
            UpdateAccountingTabState();
        }

        private void ClearAccountingSummaryDisplay()
        {
            CylindersSoldText.Text = "0";
            AcctExpectedRefillsText.Text = "0";
            RefillsCountText.Text = "0 / 0";
            UnsoldFilledText.Text = "0";
            EmptiesReturnedText.Text = "0";
            RefillTotalAmountOutput.Text = string.Empty;
            MiscTotalAmountOutput.Text = string.Empty;
            MiscCashAmountOutput.Text = string.Empty;
            MiscOnlineAmountOutput.Text = string.Empty;
            MiscGPayAmountOutput.Text = string.Empty;
            CashRefillPaymentInput.Text = string.Empty;
            OnlineRefillPaymentInput.Text = string.Empty;
            GooglePayRefillPaymentInput.Text = string.Empty;
            TotalCashText.Text = "₹0";
            TotalOnlineText.Text = "₹0";
            TotalGooglePayText.Text = "₹0";
            NetCashRequiredText.Text = "₹0";
            CashCalculatorTotal.Text = "₹0";
            _accountingEntry = new AccountingEntry();
        }

        private void ResetAccountingSettlementTab()
        {
            foreach (var product in _eveningProductEntries)
            {
                product.IsSelected = false;
                product.Quantity = 0;
                product.EmptyQty = 0;
            }

            EveningDriverCombo.SelectedIndex = -1;
            EveningVehicleCombo.SelectedIndex = -1;
            MissingReasonCombo.SelectedIndex = -1;
            MissingReasonCombo.Visibility = Visibility.Collapsed;
            HotelSelectionPanel.Visibility = Visibility.Collapsed;
            HotelNameCombo.SelectedIndex = -1;
            CustomerNameInput.Clear();
            SettlementCompletedBanner.Visibility = Visibility.Collapsed;
            MorningUpliftSummaryPanel.Visibility = Visibility.Collapsed;
            NoMorningOutSelectionBanner.Visibility = _morningOutPairs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EveningSelectionPanel.IsEnabled = _morningOutPairs.Count > 0;
            EveningDriverCombo.IsEnabled = _morningOutPairs.Count > 0;
            EveningVehicleCombo.IsEnabled = _morningOutPairs.Count > 0;
            EveningReturnsPanel.IsEnabled = false;
            ReturnButton.IsEnabled = false;
            _morningUpliftByProduct.Clear();
            _morningLoad = 0;
            ClearEveningMorningQtyHints();
            UpdateEveningInTabState();
        }

        private void InitializeProductEntries()
        {
            // Plant Bulk Receive
            _plantProductEntries.Clear();
            foreach (var product in AllProducts)
            {
                _plantProductEntries.Add(new ProductEntry { ProductName = product });
            }
            PlantProductEntryList.ItemsSource = _plantProductEntries;

            // Morning Operations (Out)
            _morningProductEntries.Clear();
            foreach (var product in AllProducts)
            {
                _morningProductEntries.Add(new ProductEntry { ProductName = product });
            }
            MorningProductEntryList.ItemsSource = _morningProductEntries;

            // Evening Reconciliation (In)
            _eveningProductEntries.Clear();
            foreach (var product in AllProducts)
            {
                _eveningProductEntries.Add(new ProductEntry { ProductName = product });
            }
            EveningProductEntryList.ItemsSource = _eveningProductEntries;
        }

        private void LoadAdminLists()
        {
            var drivers = _dbService.GetDrivers();
            DriverListBox.ItemsSource = drivers;

            var vehicles = _dbService.GetVehicles();
            VehicleListBox.ItemsSource = vehicles;

            var warehouses = _dbService.GetWarehouses();
            WarehouseListBox.ItemsSource = warehouses;
        }

        // REFRESH GRID: Called when warehouse changes - Updates all stock grids
        private void RefreshStockGrid()
        {
            if (!TryGetSelectedWarehouseId(out int warehouseId))
                return;

            var stock = _dbService.GetAllStockForWarehouse(warehouseId);

            StockDataGrid.ItemsSource = null;
            StockDataGrid.ItemsSource = stock;

            StockDataGridEvening.ItemsSource = null;
            StockDataGridEvening.ItemsSource = stock;

            PlantStockDataGrid.ItemsSource = null;
            PlantStockDataGrid.ItemsSource = stock;

            RefreshMasterStockGrid(warehouseId);
        }

        private void RefreshMasterStockGrid(int warehouseId)
        {
            if (!IsOwner()) return;
            _masterStockRows = _dbService.GetAllStockForWarehouse(warehouseId);
            MasterStockDataGrid.ItemsSource = null;
            MasterStockDataGrid.ItemsSource = _masterStockRows;
        }

        private void ReceivePlantLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate warehouse selection first
                if (WarehouseCombo.SelectedValue == null)
                {
                    MessageBox.Show("⚠️ Please select a Warehouse first!", "Warehouse Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selectedProducts = _plantProductEntries.Where(p => p.IsSelected && p.Quantity > 0).ToList();
                if (selectedProducts.Count == 0)
                {
                    MessageBox.Show("Please select at least one product with quantity.", "No Products Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int warehouseId = (int)WarehouseCombo.SelectedValue;
                string reason = (PlantShortageReason.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "N/A";

                bool shortageDetected = selectedProducts.Any(p => p.Quantity < 0);

                // Ensure return type to IOCL is selected
                if (PlantReturnTypeCombo.SelectedItem == null)
                {
                    MessageBox.Show("Please select whether you are returning empty or filled cylinders to IOCL.", "Return Type Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Verify that return quantities are entered
                var productsWithReturn = selectedProducts.Where(p => p.ReturnQty > 0).ToList();
                if (productsWithReturn.Count == 0)
                {
                    MessageBox.Show("Please enter return quantity for at least one product.", "Return Quantity Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (shortageDetected && PlantShortageReason.Visibility == Visibility.Collapsed)
                {
                    MessageBox.Show("Please select a reason for the load variance.", "Reason Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    PlantReasonPanel.Visibility = Visibility.Visible;
                    return;
                }

                using (var connection = new SqliteConnection(DatabaseService.ConnectionString))
                {
                    connection.Open();

                    foreach (var product in selectedProducts)
                    {
                        // Update stock: add received filled cylinders
                        var cmd = connection.CreateCommand();
                        cmd.CommandText = @"
                            UPDATE StockMaster 
                            SET FilledQty = FilledQty + @fIn 
                            WHERE WarehouseId = @id AND CylinderType = @type";
                        cmd.Parameters.AddWithValue("@fIn", product.Quantity);
                        cmd.Parameters.AddWithValue("@id", warehouseId);
                        cmd.Parameters.AddWithValue("@type", product.ProductName);
                        cmd.ExecuteNonQuery();

                        // Handle return to IOCL
                        if (product.ReturnQty > 0)
                        {
                            var returnTag = (PlantReturnTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
                            string returnType = returnTag == "RETURN_EMPTY" ? "Empty" : returnTag == "RETURN_FILLED" ? "Filled" : "Unknown";

                            // Deduct returned cylinders from stock
                            var returnCmd = connection.CreateCommand();
                            if (returnType == "Empty")
                            {
                                returnCmd.CommandText = @"
                                    UPDATE StockMaster 
                                    SET EmptyQty = EmptyQty - @rQty 
                                    WHERE WarehouseId = @id AND CylinderType = @type";
                            }
                            else
                            {
                                returnCmd.CommandText = @"
                                    UPDATE StockMaster 
                                    SET FilledQty = FilledQty - @rQty 
                                    WHERE WarehouseId = @id AND CylinderType = @type";
                            }
                            returnCmd.Parameters.AddWithValue("@rQty", product.ReturnQty);
                            returnCmd.Parameters.AddWithValue("@id", warehouseId);
                            returnCmd.Parameters.AddWithValue("@type", product.ProductName);
                            returnCmd.ExecuteNonQuery();

                            // Log both receipt and return
                            string detail = $"IOCL PURCHASE ({product.ProductName}): Recv {product.Quantity} Filled | Return {product.ReturnQty} {returnType}";
                            if (shortageDetected) detail += $" | Shortage Reason: {reason}";

                            var logCmd = connection.CreateCommand();
                            logCmd.CommandText = @"INSERT INTO TransactionLogs (WarehouseId, CylinderType, FilledChange, EmptyChange, TransactionType) 
                                                 VALUES (@id, @type, @f, @e, @details)";
                            logCmd.Parameters.AddWithValue("@id", warehouseId);
                            logCmd.Parameters.AddWithValue("@type", product.ProductName);

                            if (returnType == "Empty")
                            {
                                logCmd.Parameters.AddWithValue("@f", product.Quantity);
                                logCmd.Parameters.AddWithValue("@e", -product.ReturnQty);
                            }
                            else
                            {
                                logCmd.Parameters.AddWithValue("@f", product.Quantity - product.ReturnQty);
                                logCmd.Parameters.AddWithValue("@e", 0);
                            }

                            logCmd.Parameters.AddWithValue("@details", detail);
                            logCmd.ExecuteNonQuery();
                        }
                        else
                        {
                            // No return for this product
                            var returnTag = (PlantReturnTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
                            string returnType = returnTag == "RETURN_EMPTY" ? "Empty" : returnTag == "RETURN_FILLED" ? "Filled" : "Unknown";
                            string detail = $"IOCL PURCHASE ({product.ProductName}): Recv {product.Quantity} Filled | Return 0 {returnType}";
                            if (shortageDetected) detail += $" | Shortage Reason: {reason}";

                            var logCmd = connection.CreateCommand();
                            logCmd.CommandText = @"INSERT INTO TransactionLogs (WarehouseId, CylinderType, FilledChange, EmptyChange, TransactionType) 
                                                 VALUES (@id, @type, @f, @e, @details)";
                            logCmd.Parameters.AddWithValue("@id", warehouseId);
                            logCmd.Parameters.AddWithValue("@type", product.ProductName);
                            logCmd.Parameters.AddWithValue("@f", product.Quantity);
                            logCmd.Parameters.AddWithValue("@e", 0);
                            logCmd.Parameters.AddWithValue("@details", detail);
                            logCmd.ExecuteNonQuery();
                        }
                    }
                }

                MessageBox.Show($"✅ {selectedProducts.Count} product(s) received and {productsWithReturn.Count} product(s) returned to IOCL!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshStockGrid();
                LoadPlantPurchaseHistory();

                // Reset plant products list
                foreach (var product in _plantProductEntries)
                {
                    product.IsSelected = false;
                    product.Quantity = 0;
                    product.ReturnQty = 0;
                }

                PlantReasonPanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show("❌ Error: " + ex.Message, "Operation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Warehouse & Stock Logic

        private void LoadWarehouseData()
        {
            var warehouses = _dbService.GetWarehouses();
            WarehouseCombo.ItemsSource = warehouses;
            WarehouseCombo.DisplayMemberPath = "Name";
            WarehouseCombo.SelectedValuePath = "Id";

            if (warehouses.Count > 0 && WarehouseCombo.SelectedValue == null)
                WarehouseCombo.SelectedValue = warehouses[0].Id;
        }

        private void WarehouseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool isWarehouseSelected = WarehouseCombo.SelectedValue != null;

            SaveButton.IsEnabled = isWarehouseSelected;
            ReceivePlantLoadButton.IsEnabled = isWarehouseSelected;
            ReturnButton.IsEnabled = isWarehouseSelected;

            if (isWarehouseSelected && PlantReturnTypeCombo != null)
            {
                PlantReturnTypeCombo.SelectedIndex = 0;
            }

            if (isWarehouseSelected)
            {
                RefreshStockGrid();
                LoadPlantPurchaseHistory();
            }

            RefreshEveningInFromDatabase();
        }

        private void SaveMasterStockButton_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOwner("update Master Stock"))
                return;

            if (WarehouseCombo.SelectedValue == null)
            {
                MessageBox.Show("⚠️ Please select a Warehouse first!", "Warehouse Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int warehouseId = (int)WarehouseCombo.SelectedValue;
            if (_masterStockRows == null || _masterStockRows.Count == 0)
            {
                MessageBox.Show("No stock rows found for this warehouse.", "Nothing to Save", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Basic validation
            foreach (var row in _masterStockRows)
            {
                if (row.FilledQty < 0 || row.EmptyQty < 0)
                {
                    MessageBox.Show("Filled/Empty quantities cannot be negative.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var confirm = MessageBox.Show(
                "This will SET the warehouse stock to the entered values (absolute). Proceed?",
                "Confirm Master Stock Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var username = App.CurrentUser?.Username ?? "unknown";
                foreach (var row in _masterStockRows)
                {
                    _dbService.SetMasterStockAbsolute(warehouseId, row.CylinderType, row.FilledQty, row.EmptyQty, username);
                }

                RefreshStockGrid();
                MessageBox.Show("✅ Master stock updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("❌ Error: " + ex.Message, "Operation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearForm()
        {
            foreach (var product in _morningProductEntries)
            {
                product.IsSelected = false;
                product.Quantity = 0;
            }

            foreach (var product in _eveningProductEntries)
            {
                product.IsSelected = false;
                product.Quantity = 0;
                product.EmptyQty = 0;
            }

            MissingReasonCombo.SelectedIndex = -1;
            CustomerNameInput.Clear();
        }

        #endregion

        #region Transaction Actions

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (WarehouseCombo.SelectedValue == null)
            {
                MessageBox.Show("⚠️ Please select a Warehouse first!", "Warehouse Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryGetSelectedDriverVehicle(MorningDriverCombo, MorningVehicleCombo,
                out int driverId, out int vehicleId, out string driver, out string vehicle))
            {
                MessageBox.Show("Please select both Driver and Vehicle before saving morning out.", "Driver/Vehicle Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int warehouseId = (int)WarehouseCombo.SelectedValue;

            if (_dbService.IsSettlementCompleted(warehouseId, driverId, vehicleId, CurrentBusinessDateString))
            {
                MessageBox.Show($"Accounting and settlement already completed for {driver} / {vehicle} on {_businessDate:dd MMM yyyy}.", "Already Completed", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateMorningOperationState();
                return;
            }

            var selectedProducts = _morningProductEntries.Where(p => p.IsSelected && p.Quantity > 0).ToList();

            if (selectedProducts.Count == 0)
            {
                MessageBox.Show("Please select at least one product with quantity.", "No Products Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string transactionDetail = $"Morning Out | Driver: {driver} | Vehicle: {vehicle} | Date: {CurrentBusinessDateString}";

            _morningUpliftByProduct.Clear();
            var upliftsToSave = new Dictionary<string, int>();

            foreach (var product in selectedProducts)
            {
                _morningLoad = product.Quantity;
                _morningUpliftByProduct[product.ProductName] = product.Quantity;
                upliftsToSave[product.ProductName] = product.Quantity;
                _dbService.UpdateStock(warehouseId, product.ProductName, -product.Quantity, 0, transactionDetail);
            }

            _dbService.SaveMorningUplifts(warehouseId, driverId, vehicleId, CurrentBusinessDateString, upliftsToSave);

            RefreshStockGrid();

            foreach (var product in _morningProductEntries)
            {
                product.IsSelected = false;
                product.Quantity = 0;
            }

            MessageBox.Show($"✅ {selectedProducts.Count} product(s) sent out successfully for {driver} / {vehicle}!\n\nEvening In is now ready for this trip.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            RefreshEveningInFromDatabase(warehouseId);
        }

        private void ReturnButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (WarehouseCombo.SelectedValue == null)
                {
                    MessageBox.Show("⚠️ Please select a Warehouse first!", "Warehouse Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!TryGetSelectedDriverVehicle(EveningDriverCombo, EveningVehicleCombo,
                    out int driverId, out int vehicleId, out string driver, out string vehicle))
                {
                    MessageBox.Show("Please select both Driver and Vehicle before completing settlement.", "Driver/Vehicle Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int warehouseId = (int)WarehouseCombo.SelectedValue;

                if (_dbService.IsSettlementCompleted(warehouseId, driverId, vehicleId, CurrentBusinessDateString))
                {
                    UpdateAccountingSettlementState();
                    MessageBox.Show($"Accounting and settlement already completed for {driver} / {vehicle} on {_businessDate:dd MMM yyyy}.", "Already Completed", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!_dbService.HasMorningOut(warehouseId, driverId, vehicleId, CurrentBusinessDateString))
                {
                    MessageBox.Show($"No Morning Out entry found for {driver} / {vehicle} on {_businessDate:dd MMM yyyy}.\n\nPlease save Morning Out first before Evening In.", "Morning Out Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                LoadMorningUpliftForEveningSelection();

                var selectedProducts = _eveningProductEntries.Where(p => p.IsSelected && (p.Quantity > 0 || p.EmptyQty > 0)).ToList();
                if (selectedProducts.Count == 0)
                {
                    MessageBox.Show("Please select at least one product with return quantity.", "No Products Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var shortages = new Dictionary<string, (int Uplifted, int Returned, int Missing)>();

                foreach (var product in selectedProducts)
                {
                    if (_morningUpliftByProduct.TryGetValue(product.ProductName, out int upliftedQty))
                    {
                        int emptyReturned = product.Quantity;
                        int filledReturned = product.EmptyQty;
                        int totalReturned = emptyReturned + filledReturned;

                        if (totalReturned < upliftedQty)
                        {
                            shortages[product.ProductName] = (upliftedQty, totalReturned, upliftedQty - totalReturned);
                        }
                    }
                }

                if (shortages.Count > 0)
                {
                    _pendingShortages = shortages.ToDictionary(s => s.Key, s => s.Value.Missing);
                    MissingReasonCombo.Visibility = Visibility.Visible;
                    string shortageDetails = "Cylinder Shortage Detected:\n\n";
                    foreach (var shortage in shortages)
                    {
                        shortageDetails += $"{shortage.Key}: Uplifted {shortage.Value.Uplifted}, Returned {shortage.Value.Returned}, Missing {shortage.Value.Missing}\n";
                    }
                    shortageDetails += "\nPlease select a reason from the dropdown below.";
                    MessageBox.Show(shortageDetails, "⚠️ Shortage Detected", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _pendingShortages.Clear();
                ProcessEveningReconciliation(selectedProducts, warehouseId, driverId, vehicleId, driver, vehicle, "", "");
            }
            catch (Exception ex)
            {
                MessageBox.Show("❌ Error: " + ex.Message, "Operation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcessEveningReconciliation(List<ProductEntry> selectedProducts, int warehouseId,
            int driverId, int vehicleId, string driver, string vehicle, string reason, string hotelName)
        {
            try
            {
                string customer = CustomerNameInput.Text;

                using (var connection = new SqliteConnection(DatabaseService.ConnectionString))
                {
                    connection.Open();

                    foreach (var product in selectedProducts)
                    {
                        int emptyReturned = product.Quantity;
                        int filledReturned = product.EmptyQty;

                        var command = connection.CreateCommand();
                        command.CommandText = @"
                            UPDATE StockMaster 
                            SET EmptyQty = EmptyQty + @empty,
                                FilledQty = FilledQty + @filled
                            WHERE WarehouseId = @id AND CylinderType = @type";
                        command.Parameters.AddWithValue("@empty", emptyReturned);
                        command.Parameters.AddWithValue("@filled", filledReturned);
                        command.Parameters.AddWithValue("@id", warehouseId);
                        command.Parameters.AddWithValue("@type", product.ProductName);
                        command.ExecuteNonQuery();

                        string logDetails = $"Accounting Settlement: {emptyReturned} Empty + {filledReturned} Filled ({product.ProductName})";
                        logDetails += $" | Driver: {driver} | Vehicle: {vehicle} | Date: {CurrentBusinessDateString}";

                        if (!string.IsNullOrEmpty(reason))
                            logDetails += $" | Reason: {reason}";
                        if (!string.IsNullOrEmpty(hotelName))
                            logDetails += $" | Hotel: {hotelName}";
                        if (!string.IsNullOrEmpty(customer))
                            logDetails += $" | Customer: {customer}";

                        var logCmd = connection.CreateCommand();
                        logCmd.CommandText = @"INSERT INTO TransactionLogs (WarehouseId, CylinderType, FilledChange, EmptyChange, TransactionType) 
                                             VALUES (@id, @type, @f, @e, @details)";
                        logCmd.Parameters.AddWithValue("@id", warehouseId);
                        logCmd.Parameters.AddWithValue("@type", product.ProductName);
                        logCmd.Parameters.AddWithValue("@f", filledReturned);
                        logCmd.Parameters.AddWithValue("@e", emptyReturned);
                        logCmd.Parameters.AddWithValue("@details", logDetails);
                        logCmd.ExecuteNonQuery();
                    }
                }

                _dbService.MarkSettlementCompleted(warehouseId, driverId, vehicleId, CurrentBusinessDateString);

                MessageBox.Show($"✅ Accounting and settlement completed for {driver} / {vehicle} on {_businessDate:dd MMM yyyy}.", "Settlement Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshStockGrid();
                RefreshSettledPairs();
                ResetAccountingSettlementTab();
                RefreshEveningCombos();
                ApplyWarehouseGate();
                RefreshAccountingFromDatabase();
            }
            catch (Exception ex)
            {
                MessageBox.Show("❌ Error during reconciliation: " + ex.Message, "Operation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetComboBoxItemContent(object? item)
        {
            if (item is ComboBoxItem cbi)
                return cbi.Content?.ToString() ?? "";
            return item?.ToString() ?? "";
        }

        private void TryPostHotelBakiToLedger(string hotelName, string reason)
        {
            if (!reason.Contains("HOTEL BAKI", StringComparison.OrdinalIgnoreCase))
                return;

            if (!_pendingShortages.TryGetValue(Commercial19kgProduct, out int missing) || missing <= 0)
                return;

            string user = App.CurrentUser?.Username ?? "system";
            bool recorded = _dbService.TryRecordHotelBaki(
                missing,
                hotelName,
                $"Auto from Evening Reconciliation — {missing} x {Commercial19kgProduct}",
                user);

            if (recorded)
            {
                LoadCommercialLedger();
                MessageBox.Show(
                    $"Commercial ledger updated for {hotelName}:\n+{missing} empty 19kg cylinder(s) due.",
                    "Ledger Updated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Handle reason selection for shortages
        /// </summary>
        private void MissingReasonCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MissingReasonCombo.SelectedItem is ComboBoxItem selectedItem && MissingReasonCombo.Visibility == Visibility.Visible)
            {
                string tag = selectedItem.Tag?.ToString() ?? "";
                string reasonText = selectedItem.Content?.ToString() ?? "";

                // Show hotel selection only for HOTEL_BAKI reason
                if (tag == "HOTEL_BAKI")
                {
                    HotelSelectionPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    HotelSelectionPanel.Visibility = Visibility.Collapsed;
                    HotelNameCombo.SelectedIndex = -1;
                }

                // Ask for confirmation and process reconciliation if reason is selected
                MessageBoxResult result = MessageBox.Show(
                    $"Shortage reason: {reasonText}\n\nProceed with reconciliation?",
                    "Confirm Shortage Reason",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Check if hotel selection is required but not selected
                    if (tag == "HOTEL_BAKI" && HotelNameCombo.SelectedIndex < 0)
                    {
                        MessageBox.Show("Please select a hotel from the list.", "Hotel Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var selectedProducts = _eveningProductEntries.Where(p => p.IsSelected && (p.Quantity > 0 || p.EmptyQty > 0)).ToList();
                    if (selectedProducts.Count > 0 && WarehouseCombo.SelectedValue is int warehouseId)
                    {
                        if (!TryGetSelectedDriverVehicle(EveningDriverCombo, EveningVehicleCombo,
                            out int driverId, out int vehicleId, out string driver, out string vehicle))
                        {
                            MessageBox.Show("Please select both Driver and Vehicle.", "Driver/Vehicle Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        string hotelName = GetComboBoxItemContent(HotelNameCombo.SelectedItem);
                        ProcessEveningReconciliation(selectedProducts, warehouseId, driverId, vehicleId, driver, vehicle, reasonText, hotelName);
                        TryPostHotelBakiToLedger(hotelName, reasonText);

                        MissingReasonCombo.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private void DayEndButton_Click(object sender, RoutedEventArgs e)
        {
            if (WarehouseCombo.SelectedValue is not int warehouseId)
            {
                MessageBox.Show("Please select a warehouse before closing the day.", "Warehouse Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var completedToday = _dbService.GetCompletedSettlementsForDate(warehouseId, CurrentBusinessDateString);
            string summary = completedToday.Count > 0
                ? $"{completedToday.Count} driver/vehicle settlement(s) completed for {_businessDate:dd MMM yyyy}."
                : "No driver/vehicle settlements have been recorded for this working date.";

            var result = MessageBox.Show(
                $"Complete day-end for {_businessDate:dddd, dd MMM yyyy}?\n\n{summary}\n\nThe working date will move to the next day.",
                "Confirm Day End",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            _businessDate = _dbService.AdvanceBusinessDate();
            UpdateDateDisplays();
            AcctDatePicker.SelectedDate = _businessDate;
            AcctHistoryFromDate.SelectedDate = _businessDate;
            AcctHistoryToDate.SelectedDate = _businessDate;
            ResetAccountingSettlementTab();
            ClearForm();
            MorningDriverCombo.SelectedIndex = -1;
            MorningVehicleCombo.SelectedIndex = -1;
            RefreshSettledPairs();
            RefreshMorningOutPairs();
            RefreshMorningCombos();
            RefreshEveningCombos();
            UpdateMorningOperationState();
            UpdateAccountingSettlementState();
            ResetAccountingTabForm();
            RefreshAccountingCombos();
            ApplyWarehouseGate();

            MessageBox.Show($"Day end completed. Working date is now {_businessDate:dddd, dd MMM yyyy}.", "Day End Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Commercial Ledger

        private void LoadCommercialLedger()
        {
            var summaries = _dbService.GetCommercialCustomerSummaries();
            LedgerCustomerGrid.ItemsSource = summaries;

            decimal totalAmount = summaries.Sum(s => s.AmountDue);
            int totalEmpty = summaries.Sum(s => s.EmptyCylindersDue);
            LedgerTotalAmountDueText.Text = $"₹{totalAmount:N2}";
            LedgerTotalEmptyDueText.Text = totalEmpty.ToString();

            var customers = _dbService.GetCommercialCustomers();
            LedgerCustomerCombo.ItemsSource = customers;
            if (customers.Count > 0 && LedgerCustomerCombo.SelectedIndex < 0)
                LedgerCustomerCombo.SelectedIndex = 0;

            RefreshLedgerHistory();
            UpdateLedgerSelectedCustomerSummary();
        }

        private void RefreshLedgerHistory()
        {
            int? customerId = null;
            if (LedgerFilterByCustomerCheck.IsChecked == true &&
                LedgerCustomerGrid.SelectedItem is CommercialCustomerSummary selected)
            {
                customerId = selected.Id;
            }

            LedgerHistoryGrid.ItemsSource = _dbService.GetCommercialLedgerEntries(customerId);
        }

        private void UpdateLedgerSelectedCustomerSummary()
        {
            if (LedgerCustomerGrid.SelectedItem is CommercialCustomerSummary s)
            {
                LedgerSelectedCustomerSummaryText.Text =
                    $"{s.Name} — Payment due: ₹{s.AmountDue:N2} | Empty 19kg due: {s.EmptyCylindersDue}" +
                    (s.LastTransactionAt.HasValue ? $" | Last activity: {s.LastTransactionAt:dd-MMM-yyyy HH:mm}" : "");
            }
            else
            {
                LedgerSelectedCustomerSummaryText.Text = "Select a customer to view balance details.";
            }
        }

        private void LedgerRefreshBtn_Click(object sender, RoutedEventArgs e) => LoadCommercialLedger();

        private void LedgerCustomerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LedgerCustomerGrid.SelectedItem is CommercialCustomerSummary s)
            {
                LedgerCustomerCombo.SelectedValue = s.Id;
            }
            UpdateLedgerSelectedCustomerSummary();
            if (LedgerFilterByCustomerCheck.IsChecked == true)
                RefreshLedgerHistory();
        }

        private void LedgerFilterChanged(object sender, RoutedEventArgs e) => RefreshLedgerHistory();

        private void LedgerEntryTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LedgerEntryTypeCombo.SelectedItem is not ComboBoxItem item) return;
            string tag = item.Tag?.ToString() ?? "";

            switch (tag)
            {
                case "PaymentReceived":
                    LedgerAmountLabel.Content = "Payment Amount (₹):";
                    LedgerQtyLabel.Visibility = Visibility.Collapsed;
                    LedgerQtyInput.Visibility = Visibility.Collapsed;
                    break;
                case "EmptyReturned":
                    LedgerAmountLabel.Visibility = Visibility.Collapsed;
                    LedgerAmountInput.Visibility = Visibility.Collapsed;
                    LedgerQtyLabel.Content = "Empty 19kg Returned (Qty):";
                    LedgerQtyLabel.Visibility = Visibility.Visible;
                    LedgerQtyInput.Visibility = Visibility.Visible;
                    break;
                case "Adjustment":
                    LedgerAmountLabel.Content = "Amount Change (₹, +/-):";
                    LedgerAmountLabel.Visibility = Visibility.Visible;
                    LedgerAmountInput.Visibility = Visibility.Visible;
                    LedgerQtyLabel.Content = "Empty Cylinder Change (+/-):";
                    LedgerQtyLabel.Visibility = Visibility.Visible;
                    LedgerQtyInput.Visibility = Visibility.Visible;
                    break;
                default: // CreditSale19kg
                    LedgerAmountLabel.Content = "Sale Amount (₹):";
                    LedgerAmountLabel.Visibility = Visibility.Visible;
                    LedgerAmountInput.Visibility = Visibility.Visible;
                    LedgerQtyLabel.Content = "19kg Cylinders (Qty):";
                    LedgerQtyLabel.Visibility = Visibility.Visible;
                    LedgerQtyInput.Visibility = Visibility.Visible;
                    break;
            }
        }

        private bool TryGetLedgerCustomerId(out int customerId)
        {
            if (LedgerCustomerCombo.SelectedValue is int id)
            {
                customerId = id;
                return true;
            }

            if (LedgerCustomerCombo.SelectedItem is CommercialCustomer selected)
            {
                customerId = selected.Id;
                return true;
            }

            string typed = LedgerCustomerCombo.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(typed))
            {
                var found = _dbService.FindCommercialCustomerIdByName(typed)
                    ?? _dbService.FindCommercialCustomerIdByConsumerId(typed)
                    ?? _dbService.FindCommercialCustomerIdByConsumerNo(typed);
                if (found.HasValue)
                {
                    customerId = found.Value;
                    return true;
                }
            }

            customerId = 0;
            return false;
        }

        private void LedgerSaveEntryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetLedgerCustomerId(out int customerId))
            {
                MessageBox.Show("Please select or type a customer name from the imported register.", "Customer Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (LedgerEntryTypeCombo.SelectedItem is not ComboBoxItem typeItem)
            {
                MessageBox.Show("Please select a transaction type.", "Type Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string tag = typeItem.Tag?.ToString() ?? "";
            if (!Enum.TryParse<CommercialLedgerEntryType>(tag, out var entryType))
            {
                MessageBox.Show("Invalid transaction type.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            decimal amountChange = 0;
            int emptyChange = 0;
            int filled19kg = 0;

            if (entryType == CommercialLedgerEntryType.CreditSale19kg)
            {
                if (!decimal.TryParse(LedgerAmountInput.Text, out var saleAmount) || saleAmount <= 0)
                {
                    MessageBox.Show("Enter a valid sale amount greater than zero.", "Amount Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!int.TryParse(LedgerQtyInput.Text, out int qty) || qty <= 0)
                {
                    MessageBox.Show("Enter cylinder quantity greater than zero.", "Quantity Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                amountChange = saleAmount;
                emptyChange = qty;
                filled19kg = qty;
            }
            else if (entryType == CommercialLedgerEntryType.PaymentReceived)
            {
                if (!decimal.TryParse(LedgerAmountInput.Text, out var payment) || payment <= 0)
                {
                    MessageBox.Show("Enter a valid payment amount greater than zero.", "Payment Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                amountChange = -payment;
            }
            else if (entryType == CommercialLedgerEntryType.EmptyReturned)
            {
                if (!int.TryParse(LedgerQtyInput.Text, out int returned) || returned <= 0)
                {
                    MessageBox.Show("Enter how many empty cylinders were returned.", "Quantity Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                emptyChange = -returned;
            }
            else // Adjustment
            {
                decimal.TryParse(LedgerAmountInput.Text, out amountChange);
                int.TryParse(LedgerQtyInput.Text, out emptyChange);
            }

            try
            {
                _dbService.AddCommercialLedgerEntry(
                    customerId,
                    entryType,
                    amountChange,
                    emptyChange,
                    filled19kg,
                    LedgerNotesInput.Text,
                    App.CurrentUser?.Username);

                LedgerAmountInput.Clear();
                LedgerQtyInput.Clear();
                LedgerNotesInput.Clear();

                LoadCommercialLedger();
                MessageBox.Show("Ledger entry saved.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save ledger entry: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LedgerAddCustomerBtn_Click(object sender, RoutedEventArgs e)
        {
            string name = LedgerNewCustomerNameInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Enter a customer name.", "Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var existingId = _dbService.FindCommercialCustomerIdByName(name);
                if (existingId.HasValue)
                {
                    MessageBox.Show($"Customer \"{name}\" already exists in the register.", "Already Exists", MessageBoxButton.OK, MessageBoxImage.Information);
                    LedgerCustomerCombo.SelectedValue = existingId.Value;
                    return;
                }

                int id = _dbService.AddCommercialCustomer(name, LedgerNewCustomerPhoneInput.Text.Trim());
                LedgerNewCustomerNameInput.Clear();
                LedgerNewCustomerPhoneInput.Clear();
                LoadCommercialLedger();
                LedgerCustomerCombo.SelectedValue = id;
                MessageBox.Show($"Customer \"{name}\" added.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not add customer: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LedgerImportRegisterBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Customer Register",
                Filter = "Customer register (CSV)|*.csv|Excel CSV|*.csv;*.txt|All files|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                bool commercialOnly = LedgerImportCommercialOnlyCheck.IsChecked == true;
                var csvText = File.ReadAllText(dialog.FileName, Encoding.UTF8);
                var rows = CommercialCustomerImporter.ParseCsv(csvText, commercialOnly);

                if (rows.Count == 0)
                {
                    MessageBox.Show(
                        commercialOnly
                            ? "No Commercial / 19kg customers found.\n\nUncheck 'Import only Commercial' to import all rows, or verify Consumer Type / Product columns."
                            : "No rows with Consumer Name found.\n\nUse your IOCL register CSV with header row (Consumer Name column required).",
                        "Import",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var importResult = _dbService.ImportCommercialCustomers(rows, App.CurrentUser?.Username);
                LoadCommercialLedger();
                ShowImportResult(importResult, dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Import failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LedgerDownloadTemplateBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Sample Customer Register",
                Filter = "CSV file (*.csv)|*.csv",
                FileName = "commercial-customer-register-sample.csv"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                File.WriteAllText(dialog.FileName, CommercialCustomerImporter.SampleCsvContent, Encoding.UTF8);
                MessageBox.Show(
                    $"Sample saved to:\n{dialog.FileName}\n\nFill in your customers and use Import Register.",
                    "Sample CSV",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save sample: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowImportResult(CommercialCustomerImportResult result, string fileName)
        {
            string status = $"Imported {result.TotalRead} row(s) from {Path.GetFileName(fileName)}: " +
                            $"{result.Added} added, {result.Updated} updated, {result.Skipped} skipped.";
            if (result.OpeningBalancesPosted > 0)
                status += $" {result.OpeningBalancesPosted} opening balance(s) posted.";
            LedgerImportStatusText.Text = status;

            string message = status;
            if (result.HasErrors)
            {
                message += "\n\nErrors:\n" + string.Join("\n", result.Errors.Take(10));
                if (result.Errors.Count > 10)
                    message += $"\n... and {result.Errors.Count - 10} more.";
            }

            MessageBox.Show(
                message,
                result.HasErrors ? "Import Completed with Errors" : "Import Complete",
                MessageBoxButton.OK,
                result.HasErrors ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        #endregion

        #region Reporting & Totals Logic

        // Admin button handlers (Owner only)
        private void DriverListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DriverListBox.SelectedItem is Driver driver)
                NewDriverName.Text = driver.Name;
        }

        private void VehicleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VehicleListBox.SelectedItem is Vehicle vehicle)
                NewVehicleNumber.Text = vehicle.VehicleNumber;
        }

        private void AddDriverBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOwner("add drivers")) return;
            var name = NewDriverName.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Enter driver name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _dbService.AddDriver(name);
                NewDriverName.Clear();
                LoadLogisticsData();
                LoadAdminLists();
                MessageBox.Show("Driver added.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error adding driver: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateDriverBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOwner("update drivers")) return;

            if (DriverListBox.SelectedItem is not Driver driver)
            {
                MessageBox.Show("Select a driver to update.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var name = NewDriverName.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Enter driver name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _dbService.UpdateDriver(driver.Id, name);
                LoadLogisticsData();
                LoadAdminLists();
                MessageBox.Show("Driver updated.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error updating driver: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveDriverBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOwner("remove drivers")) return;
            if (DriverListBox.SelectedItem is Driver drv)
            {
                var res = MessageBox.Show($"Remove driver '{drv.Name}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    _dbService.RemoveDriver(drv.Id);
                    LoadLogisticsData();
                    LoadAdminLists();
                }
            }
            else
            {
                MessageBox.Show("Select a driver to remove.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AddVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOwner("add vehicles")) return;
            var num = NewVehicleNumber.Text?.Trim();
            if (string.IsNullOrWhiteSpace(num))
            {
                MessageBox.Show("Enter vehicle number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _dbService.AddVehicle(num);
                NewVehicleNumber.Clear();
                LoadLogisticsData();
                LoadAdminLists();
                MessageBox.Show("Vehicle added.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error adding vehicle: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOwner("update vehicles")) return;

            if (VehicleListBox.SelectedItem is not Vehicle vehicle)
            {
                MessageBox.Show("Select a vehicle to update.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var num = NewVehicleNumber.Text?.Trim();
            if (string.IsNullOrWhiteSpace(num))
            {
                MessageBox.Show("Enter vehicle number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _dbService.UpdateVehicle(vehicle.Id, num);
                LoadLogisticsData();
                LoadAdminLists();
                MessageBox.Show("Vehicle updated.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error updating vehicle: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveVehicleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOwner("remove vehicles")) return;
            if (VehicleListBox.SelectedItem is Vehicle v)
            {
                var res = MessageBox.Show($"Remove vehicle '{v.VehicleNumber}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    _dbService.RemoveVehicle(v.Id);
                    LoadLogisticsData();
                    LoadAdminLists();
                }
            }
            else
            {
                MessageBox.Show("Select a vehicle to remove.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AddWarehouseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOwner("add warehouses")) return;
            var name = NewWarehouseName.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Enter warehouse name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _dbService.AddWarehouse(name);
                NewWarehouseName.Clear();
                LoadWarehouseData();
                LoadAdminLists();
                _dbService.EnsureStockRowsExist();
                ApplyWarehouseGate();

                MessageBox.Show("Warehouse added. All operational tabs are now enabled!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error adding warehouse: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveWarehouseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOwner("remove warehouses")) return;
            if (WarehouseListBox.SelectedItem is Warehouse wh)
            {
                var res = MessageBox.Show($"Remove warehouse '{wh.Name}'?\nThis will affect all related stock data.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    _dbService.RemoveWarehouse(wh.Id);
                    LoadWarehouseData();
                    LoadAdminLists();
                    WarehouseCombo.SelectedIndex = -1;
                    RefreshStockGrid();

                    // Check if any warehouses remain
                    var remainingWarehouses = _dbService.GetWarehouses();
                    if (remainingWarehouses.Count == 0)
                    {
                        ApplyWarehouseGate();
                        MessageBox.Show("No warehouses remain. All tabs except Admin have been disabled.\nPlease add a warehouse to continue.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Warehouse removed.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            else
            {
                MessageBox.Show("Select a warehouse to remove.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RefreshReportBtn_Click(object sender, RoutedEventArgs e)
        {
            FromDatePicker.SelectedDate = null;
            ToDatePicker.SelectedDate = null;
            LoadTransactionReports();
        }

        private void FilterBtn_Click(object sender, RoutedEventArgs e)
        {
            if (FromDatePicker.SelectedDate == null || ToDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Select both dates to filter.");
                return;
            }
            LoadTransactionReports(FromDatePicker.SelectedDate.Value, ToDatePicker.SelectedDate.Value);
        }

        private void LoadTransactionReports(DateTime? from = null, DateTime? to = null)
        {
            List<TransactionLog> logs = new List<TransactionLog>();
            try
            {
                using (var conn = new SqliteConnection(DatabaseService.ConnectionString))
                {
                    conn.Open();
                    var cmd = conn.CreateCommand();

                    // SELECT the CylinderType column as well
                    string sql = @"
                        SELECT t.Timestamp, w.Name, t.CylinderType, t.TransactionType, t.FilledChange, t.EmptyChange 
                        FROM TransactionLogs t
                        LEFT JOIN Warehouses w ON t.WarehouseId = w.Id";

                    if (from.HasValue && to.HasValue)
                    {
                        sql += " WHERE date(t.Timestamp) BETWEEN @f AND @t";
                        cmd.Parameters.AddWithValue("@f", from.Value.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@t", to.Value.ToString("yyyy-MM-dd"));
                    }

                    cmd.CommandText = sql + " ORDER BY t.Timestamp DESC";

                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            logs.Add(new TransactionLog
                            {
                                Timestamp = r.GetString(0),
                                WarehouseName = r.IsDBNull(1) ? "Unknown" : r.GetString(1),
                                CylinderType = r.GetString(2),
                                TransactionType = r.GetString(3),
                                FilledChange = r.GetInt32(4),
                                EmptyChange = r.GetInt32(5)
                            });
                        }
                    }
                }

                ReportsGrid.ItemsSource = logs;
                TotalFilledText.Text = logs.Sum(x => x.FilledChange).ToString();
                TotalEmptyText.Text = logs.Sum(x => x.EmptyChange).ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Report Load Error: " + ex.Message);
            }
        }

        #endregion

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var reportData = ReportsGrid.ItemsSource as List<GasAgencyManager.Models.TransactionLog>;
                if (reportData == null || reportData.Count == 0)
                {
                    MessageBox.Show("No data available to export!");
                    return;
                }

                Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    FileName = $"StockReport_{DateTime.Now:ddMMyyyy}.csv"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var csv = new System.Text.StringBuilder();
                    csv.AppendLine("Timestamp,Warehouse,Cylinder Type,Filled Change,Empty Change,Details");
                    foreach (var item in reportData)
                    {
                        csv.AppendLine($"{item.Timestamp},{item.WarehouseName},{item.CylinderType},{item.FilledChange},{item.EmptyChange},\"{item.TransactionType}\"");
                    }
                    System.IO.File.WriteAllText(saveFileDialog.FileName, csv.ToString());
                    MessageBox.Show("Report exported successfully!");
                }
            }
            catch (Exception ex) { MessageBox.Show("Export Error: " + ex.Message); }
        }

        private void AcctDriverVehicle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshingCombos) return;

            try
            {
                if (sender == AcctDriverCombo)
                    RefreshAccountingCombos(refreshDriverList: false, refreshVehicleList: true);
                else if (sender == AcctVehicleCombo)
                    RefreshAccountingCombos(refreshDriverList: true, refreshVehicleList: false);
                else
                    RefreshAccountingCombos();

                UpdateAccountingTabState();

                if (TryGetSelectedDriverVehicle(AcctDriverCombo, AcctVehicleCombo,
                    out int driverId, out int vehicleId, out _, out _)
                    && _accountingPendingPairs.Contains((driverId, vehicleId)))
                {
                    PopulateAccountingSummary();
                }
                else
                {
                    ClearAccountingSummaryDisplay();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error updating Accounting tab: {ex.Message}",
                    "Accounting Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void PopulateAccountingSummary()
        {
            if (!TryGetSelectedWarehouseId(out int warehouseId))
                return;

            if (!TryGetSelectedDriverVehicle(AcctDriverCombo, AcctVehicleCombo,
                out int driverId, out int vehicleId, out _, out _))
                return;

            if (!_accountingPendingPairs.Contains((driverId, vehicleId)))
                return;

            var summary = _dbService.GetTripAccountingSummary(warehouseId, driverId, vehicleId, CurrentBusinessDateString);
            _expectedRefillCount = summary.RefillsSold;

            _accountingEntry.CylindersSold = summary.RefillsSold;
            _accountingEntry.RefillsSold = 0;
            CylindersSoldText.Text = summary.RefillsSold.ToString();
            AcctExpectedRefillsText.Text = summary.RefillsSold.ToString();
            UnsoldFilledText.Text = summary.UnsoldFilledReturned.ToString();
            EmptiesReturnedText.Text = summary.EmptiesReturned.ToString();
            RefillsCountText.Text = $"0 / {_expectedRefillCount}";
        }

        // Regular cylinder sales inputs were replaced with Misc Sales.
        // These handlers are kept removed to avoid compile errors when controls don't exist.

        private void MiscSalesInputs_TextChanged(object sender, TextChangedEventArgs e)
        {
            static int ParseInt(string? s) => int.TryParse(s, out var v) ? v : 0;
            static decimal ParseDecimal(string? s) => decimal.TryParse(s, out var v) ? v : 0m;

            void CalcLine(
                TextBox priceInput,
                TextBox qtyInput,
                TextBox onlineQtyInput,
                TextBox gpayQtyInput,
                TextBox cashQtyOutput,
                out decimal cashAmount,
                out decimal onlineAmount,
                out decimal gpayAmount)
            {
                var price = ParseDecimal(priceInput.Text);
                var totalQty = ParseInt(qtyInput.Text);
                var onlineQty = ParseInt(onlineQtyInput.Text);
                var gpayQty = ParseInt(gpayQtyInput.Text);

                if (onlineQty < 0) onlineQty = 0;
                if (gpayQty < 0) gpayQty = 0;
                if (totalQty < 0) totalQty = 0;

                // Auto-assume total = online + gpay (cash becomes 0) when user enters online/gpay first.
                if (totalQty == 0 && (onlineQty > 0 || gpayQty > 0))
                {
                    totalQty = onlineQty + gpayQty;
                    if (qtyInput.Text != totalQty.ToString())
                        qtyInput.Text = totalQty.ToString();
                }

                // Ensure total is at least online+gpay
                var minTotal = onlineQty + gpayQty;
                if (totalQty < minTotal)
                {
                    totalQty = minTotal;
                    if (qtyInput.Text != totalQty.ToString())
                        qtyInput.Text = totalQty.ToString();
                }

                // Clamp if needed
                if (onlineQty > totalQty) onlineQty = totalQty;
                if (gpayQty > totalQty - onlineQty) gpayQty = totalQty - onlineQty;

                var cashQty = totalQty - onlineQty - gpayQty;
                cashQtyOutput.Text = cashQty.ToString();

                cashAmount = cashQty * price;
                onlineAmount = onlineQty * price;
                gpayAmount = gpayQty * price;
            }

            CalcLine(
                MiscSbcToDbcPriceInput, MiscSbcToDbcQtyInput, MiscSbcToDbcOnlineQtyInput, MiscSbcToDbcGPayQtyInput, MiscSbcToDbcCashQtyOutput,
                out var c1, out var o1, out var g1);
            CalcLine(
                MiscNewSbcPriceInput, MiscNewSbcQtyInput, MiscNewSbcOnlineQtyInput, MiscNewSbcGPayQtyInput, MiscNewSbcCashQtyOutput,
                out var c2, out var o2, out var g2);
            CalcLine(
                MiscNewDbcPriceInput, MiscNewDbcQtyInput, MiscNewDbcOnlineQtyInput, MiscNewDbcGPayQtyInput, MiscNewDbcCashQtyOutput,
                out var c3, out var o3, out var g3);
            CalcLine(
                MiscOtherPriceInput, MiscOtherQtyInput, MiscOtherOnlineQtyInput, MiscOtherGPayQtyInput, MiscOtherCashQtyOutput,
                out var c4, out var o4, out var g4);

            var cash = c1 + c2 + c3 + c4;
            var online = o1 + o2 + o3 + o4;
            var gpay = g1 + g2 + g3 + g4;
            var total = cash + online + gpay;

            // Reuse existing "regular" fields as misc sales totals (since regular cylinder sales UI is removed).
            _accountingEntry.Cash = cash;
            _accountingEntry.Online = online;
            _accountingEntry.GPay = gpay;

            MiscTotalAmountOutput.Text = $"₹{total:F2}";
            MiscCashAmountOutput.Text = $"₹{cash:F2}";
            MiscOnlineAmountOutput.Text = $"₹{online:F2}";
            MiscGPayAmountOutput.Text = $"₹{gpay:F2}";

            UpdateAccountingDisplay();
        }
        private void CashRefillPaymentInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (decimal.TryParse(CashRefillPaymentInput.Text, out var v)) _accountingEntry.CashRefill = v;
            UpdateAccountingDisplay();
        }
        private void OnlineRefillPaymentInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (decimal.TryParse(OnlineRefillPaymentInput.Text, out var v)) _accountingEntry.OnlineRefill = v;
            UpdateAccountingDisplay();
        }
        private void GooglePayRefillPaymentInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (decimal.TryParse(GooglePayRefillPaymentInput.Text, out var v)) _accountingEntry.GPayRefill = v;
            UpdateAccountingDisplay();
        }
        private void RefillTierInputs_TextChanged(object sender, TextChangedEventArgs e)
        {
            static int ParseInt(string? s) => int.TryParse(s, out var v) ? v : 0;
            static decimal ParseDecimal(string? s) => decimal.TryParse(s, out var v) ? v : 0m;

            void CalcTier(
                TextBox priceInput,
                TextBox qtyInput,
                TextBox onlineQtyInput,
                TextBox gpayQtyInput,
                TextBox cashQtyOutput,
                out decimal cashAmount,
                out decimal onlineAmount,
                out decimal gpayAmount,
                out int totalQty)
            {
                var price = ParseDecimal(priceInput.Text);
                totalQty = ParseInt(qtyInput.Text);
                var onlineQty = ParseInt(onlineQtyInput.Text);
                var gpayQty = ParseInt(gpayQtyInput.Text);

                if (onlineQty < 0) onlineQty = 0;
                if (gpayQty < 0) gpayQty = 0;
                if (totalQty < 0) totalQty = 0;

                // If user enters online/gpay first (without total), auto-assume total = online + gpay (cash becomes 0)
                if (totalQty == 0 && (onlineQty > 0 || gpayQty > 0))
                {
                    totalQty = onlineQty + gpayQty;
                    if (qtyInput.Text != totalQty.ToString())
                        qtyInput.Text = totalQty.ToString();
                }

                // Ensure total is at least online+gpay; otherwise cash becomes negative.
                var minTotal = onlineQty + gpayQty;
                if (totalQty < minTotal)
                {
                    totalQty = minTotal;
                    if (qtyInput.Text != totalQty.ToString())
                        qtyInput.Text = totalQty.ToString();
                }

                // Clamp in case user types too much (after ensuring total)
                if (onlineQty > totalQty) onlineQty = totalQty;
                if (gpayQty > totalQty - onlineQty) gpayQty = totalQty - onlineQty;

                var cashQty = totalQty - onlineQty - gpayQty;
                cashQtyOutput.Text = cashQty.ToString();

                cashAmount = cashQty * price;
                onlineAmount = onlineQty * price;
                gpayAmount = gpayQty * price;
            }

            CalcTier(
                Refill1PriceInput, Refill1QtyInput, Refill1OnlineQtyInput, Refill1GPayQtyInput, Refill1CashQtyOutput,
                out var c1, out var o1, out var g1, out var q1);
            CalcTier(
                Refill2PriceInput, Refill2QtyInput, Refill2OnlineQtyInput, Refill2GPayQtyInput, Refill2CashQtyOutput,
                out var c2, out var o2, out var g2, out var q2);
            CalcTier(
                Refill3PriceInput, Refill3QtyInput, Refill3OnlineQtyInput, Refill3GPayQtyInput, Refill3CashQtyOutput,
                out var c3, out var o3, out var g3, out var q3);

            var cash = c1 + c2 + c3;
            var online = o1 + o2 + o3;
            var gpay = g1 + g2 + g3;

            _accountingEntry.CashRefill = cash;
            _accountingEntry.OnlineRefill = online;
            _accountingEntry.GPayRefill = gpay;
            _accountingEntry.TotalRefillAmount = cash + online + gpay;
            _accountingEntry.RefillsSold = q1 + q2 + q3;

            RefillsCountText.Text = _expectedRefillCount > 0
                ? $"{_accountingEntry.RefillsSold} / {_expectedRefillCount}"
                : _accountingEntry.RefillsSold.ToString();
            RefillTotalAmountOutput.Text = $"₹{_accountingEntry.TotalRefillAmount:F2}";
            CashRefillPaymentInput.Text = $"₹{_accountingEntry.CashRefill:F2}";
            OnlineRefillPaymentInput.Text = $"₹{_accountingEntry.OnlineRefill:F2}";
            GooglePayRefillPaymentInput.Text = $"₹{_accountingEntry.GPayRefill:F2}";

            UpdateAccountingDisplay();
        }
        private void ChangeProvidedInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (decimal.TryParse(ChangeProvidedInput.Text, out var v)) _accountingEntry.Change = v;
            UpdateAccountingDisplay();
        }
        private void FuelExpenseInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (decimal.TryParse(FuelExpenseInput.Text, out var v)) _accountingEntry.Fuel = v;
            UpdateAccountingDisplay();
        }
        private void OtherExpenseInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (decimal.TryParse(OtherExpenseInput.Text, out var v)) _accountingEntry.Other = v;
            UpdateAccountingDisplay();
        }
        private void UpdateAccountingDisplay()
        {
            TotalCashText.Text = $"₹{_accountingEntry.TotalCash:F2}";
            TotalOnlineText.Text = $"₹{_accountingEntry.TotalOnline:F2}";
            TotalGooglePayText.Text = $"₹{_accountingEntry.TotalGPay:F2}";
            TotalExpensesText.Text = $"₹{_accountingEntry.TotalExpense:F2}";
            NetCashRequiredText.Text = $"₹{_accountingEntry.NetCash:F2}";
        }
        private void CalculateCashBtn_Click(object sender, RoutedEventArgs e)
        {
            int n5 = int.TryParse(Notes5Input.Text, out var v5) ? v5 : 0;
            int n10 = int.TryParse(Notes10Input.Text, out var v10) ? v10 : 0;
            int n20 = int.TryParse(Notes20Input.Text, out var v20) ? v20 : 0;
            int n50 = int.TryParse(Notes50Input.Text, out var v50) ? v50 : 0;
            int n100 = int.TryParse(Notes100Input.Text, out var v100) ? v100 : 0;
            int n200 = int.TryParse(Notes200Input.Text, out var v200) ? v200 : 0;
            int n500 = int.TryParse(Notes500Input.Text, out var v500) ? v500 : 0;
            decimal total = n5*5 + n10*10 + n20*20 + n50*50 + n100*100 + n200*200 + n500*500;
            CashCalculatorTotal.Text = $"₹{total:F2}";
        }

        private void SaveAcctSummaryBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryGetSelectedWarehouseId(out _))
                {
                    MessageBox.Show("Please select a warehouse.", "Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!TryGetSelectedDriverVehicle(AcctDriverCombo, AcctVehicleCombo,
                    out int driverId, out int vehicleId, out string driverName, out string vehicleNumber))
                {
                    MessageBox.Show("Please select driver and vehicle.", "Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!_accountingPendingPairs.Contains((driverId, vehicleId)))
                {
                    MessageBox.Show("This driver/vehicle is not pending accounting or has already been saved.",
                        "Not Pending", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                static int ParseInt(string? s) => int.TryParse(s, out var v) ? v : 0;
                int enteredRefills = ParseInt(Refill1QtyInput.Text)
                    + ParseInt(Refill2QtyInput.Text)
                    + ParseInt(Refill3QtyInput.Text);

                if (_expectedRefillCount <= 0)
                {
                    MessageBox.Show("No expected refill count found for this trip. Re-select the driver and vehicle.",
                        "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (enteredRefills != _expectedRefillCount)
                {
                    MessageBox.Show(
                        $"Refill quantities must total exactly {_expectedRefillCount}. You entered {enteredRefills}.",
                        "Refill Validation",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (_dbService.IsAccountingSettlementExists(driverId, vehicleId, _businessDate))
                {
                    MessageBox.Show("Accounting has already been saved for this driver/vehicle today.",
                        "Already Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshAccountingFromDatabase();
                    return;
                }

                if (!_dbService.RecordAccountingSettlement(
                    driverId,
                    vehicleId,
                    _businessDate,
                    BuildAccountingSummaryText(),
                    _expectedRefillCount,
                    enteredRefills,
                    GetAccountingTotalCollection()))
                {
                    MessageBox.Show("Failed to save accounting. It may already exist for this trip.",
                        "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SaveAccountingSnapshot(createBackup: true);

                MessageBox.Show(
                    $"Accounting saved for {driverName} / {vehicleNumber} on {_businessDate:dd MMM yyyy}.",
                    "Accounting Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ResetAccountingTabForm();
                RefreshAccountingCombos();
                UpdateAccountingTabState();
                ApplyWarehouseGate();
                LoadAccountingHistoryForCurrentRange();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private decimal GetAccountingTotalCollection()
        {
            static decimal ParseMoney(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return 0m;
                var cleaned = s.Replace("₹", "").Trim();
                return decimal.TryParse(cleaned, out var v) ? v : 0m;
            }

            return ParseMoney(TotalCashText.Text)
                + ParseMoney(TotalOnlineText.Text)
                + ParseMoney(TotalGooglePayText.Text);
        }

        private void LoadAccountingHistoryForCurrentRange()
        {
            string? from = AcctHistoryFromDate.SelectedDate?.ToString("yyyy-MM-dd");
            string? to = AcctHistoryToDate.SelectedDate?.ToString("yyyy-MM-dd");
            _accountingHistory = _dbService.GetAccountingSettlementHistory(from, to);
            AcctHistoryGrid.ItemsSource = null;
            AcctHistoryGrid.ItemsSource = _accountingHistory;
            AcctHistoryDetailText.Text = _accountingHistory.Count == 0
                ? "No saved accounting records found for the selected date range."
                : "Select a saved accounting record above to view details.";
        }

        private AccountingSettlementRecord? GetSelectedAccountingHistoryRecord() =>
            AcctHistoryGrid.SelectedItem as AccountingSettlementRecord;

        private void LoadAcctHistoryBtn_Click(object sender, RoutedEventArgs e) => LoadAccountingHistoryForCurrentRange();

        private void ShowAllAcctHistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            AcctHistoryFromDate.SelectedDate = null;
            AcctHistoryToDate.SelectedDate = null;
            LoadAccountingHistoryForCurrentRange();
        }

        private void AcctHistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GetSelectedAccountingHistoryRecord() is AccountingSettlementRecord record)
                ShowAccountingHistoryDetail(record);
        }

        private void ViewAcctHistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (GetSelectedAccountingHistoryRecord() is not AccountingSettlementRecord record)
            {
                MessageBox.Show("Please select a saved accounting record.", "Selection Required",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ShowAccountingHistoryDetail(record);
        }

        private void ShowAccountingHistoryDetail(AccountingSettlementRecord record)
        {
            AcctHistoryDetailText.Text = string.IsNullOrWhiteSpace(record.SummaryText)
                ? $"Accounting saved for {record.DriverName} / {record.VehicleNumber} on {record.SettlementDateDisplay}, but detailed summary was not stored."
                : record.SummaryText;
        }

        private void PrintAcctHistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (GetSelectedAccountingHistoryRecord() is not AccountingSettlementRecord record)
            {
                MessageBox.Show("Please select a saved accounting record to print.", "Selection Required",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() != true) return;

                var doc = BuildAccountingSummaryFlowDocumentFromText(
                    string.IsNullOrWhiteSpace(record.SummaryText)
                        ? $"Accounting record: {record.DriverName} / {record.VehicleNumber} on {record.SettlementDateDisplay}"
                        : record.SummaryText);
                doc.PageHeight = printDialog.PrintableAreaHeight;
                doc.PageWidth = printDialog.PrintableAreaWidth;
                doc.PagePadding = new Thickness(40);
                doc.ColumnWidth = double.PositiveInfinity;

                printDialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator,
                    $"Accounting {record.DriverName} {record.SettlementDateDisplay}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Print failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static FlowDocument BuildAccountingSummaryFlowDocumentFromText(string text)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                PagePadding = new Thickness(40)
            };

            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 0, 0, 2) });
            }

            return doc;
        }

        private void PrintAcctSummaryBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() != true) return;

                var doc = BuildAccountingSummaryFlowDocument();
                doc.PageHeight = printDialog.PrintableAreaHeight;
                doc.PageWidth = printDialog.PrintableAreaWidth;
                doc.PagePadding = new Thickness(40);
                doc.ColumnWidth = double.PositiveInfinity;

                printDialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Accounting Summary");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Print failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildAccountingSummaryText()
        {
            static int ParseInt(string? s) => int.TryParse(s, out var v) ? v : 0;
            static decimal ParseMoney(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return 0m;
                var cleaned = s.Replace("₹", "").Trim();
                return decimal.TryParse(cleaned, out var v) ? v : 0m;
            }

            string date = AcctDatePicker.SelectedDate?.ToString("dd-MMM-yyyy") ?? "N/A";
            string driver = (AcctDriverCombo.SelectedItem as DriverComboItem)?.Name
                ?? (AcctDriverCombo.SelectedItem as Driver)?.Name ?? "N/A";
            string vehicle = (AcctVehicleCombo.SelectedItem as VehicleComboItem)?.VehicleNumber
                ?? (AcctVehicleCombo.SelectedItem as Vehicle)?.VehicleNumber ?? "N/A";

            int soldCyl = ParseInt(CylindersSoldText.Text);
            int returnedFilled = ParseInt(UnsoldFilledText.Text);
            int upliftCyl = soldCyl + returnedFilled;

            decimal totalCash = ParseMoney(TotalCashText.Text);
            decimal totalOnline = ParseMoney(TotalOnlineText.Text);
            decimal totalGPay = ParseMoney(TotalGooglePayText.Text);
            decimal totalCollection = totalCash + totalOnline + totalGPay;

            string KV(string label, string value, int pad = 30) => $"{label.PadRight(pad)} {value}";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("GAS AGENCY MANAGER - ACCOUNTING SUMMARY");
            sb.AppendLine(new string('=', 55));
            sb.AppendLine(KV("Date:", date));
            sb.AppendLine(KV("Driver:", driver));
            sb.AppendLine(KV("Vehicle:", vehicle));
            sb.AppendLine();

            sb.AppendLine("CYLINDER SUMMARY");
            sb.AppendLine(new string('-', 55));
            sb.AppendLine(KV("Total Uplift Cylinders:", upliftCyl.ToString()));
            sb.AppendLine(KV("Return Filled Cylinders:", returnedFilled.ToString()));
            sb.AppendLine(KV("Sold Cylinders:", soldCyl.ToString()));
            sb.AppendLine(KV("Empties Returned:", EmptiesReturnedText.Text));
            sb.AppendLine();

            sb.AppendLine("REFILL DETAILS (Price / Qty)");
            sb.AppendLine(new string('-', 55));
            sb.AppendLine("Tier   Price     Qty   OnlineQty   GPayQty   CashQty");
            sb.AppendLine("----   -----     ---   ---------   ------   ------");
            sb.AppendLine($"R1     {Refill1PriceInput.Text,-8} {Refill1QtyInput.Text,-5} {Refill1OnlineQtyInput.Text,-11} {Refill1GPayQtyInput.Text,-8} {Refill1CashQtyOutput.Text}");
            sb.AppendLine($"R2     {Refill2PriceInput.Text,-8} {Refill2QtyInput.Text,-5} {Refill2OnlineQtyInput.Text,-11} {Refill2GPayQtyInput.Text,-8} {Refill2CashQtyOutput.Text}");
            sb.AppendLine($"R3     {Refill3PriceInput.Text,-8} {Refill3QtyInput.Text,-5} {Refill3OnlineQtyInput.Text,-11} {Refill3GPayQtyInput.Text,-8} {Refill3CashQtyOutput.Text}");
            sb.AppendLine();
            sb.AppendLine(KV("Refills Count:", RefillsCountText.Text));
            sb.AppendLine(KV("Refill Total Amount:", RefillTotalAmountOutput.Text));
            sb.AppendLine();

            sb.AppendLine("PAYMENT SUMMARY");
            sb.AppendLine(new string('-', 55));
            sb.AppendLine(KV("Online Payment (Total):", TotalOnlineText.Text));
            sb.AppendLine(KV("Google Pay (Total):", TotalGooglePayText.Text));
            sb.AppendLine(KV("Cash Collection (Total):", TotalCashText.Text));
            sb.AppendLine(KV("Final Total Collection:", $"₹{totalCollection:F2}"));
            sb.AppendLine();

            sb.AppendLine("EXPENSES");
            sb.AppendLine(new string('-', 55));
            sb.AppendLine(KV("Change Provided by Office:", string.IsNullOrWhiteSpace(ChangeProvidedInput.Text) ? "0" : ChangeProvidedInput.Text));
            sb.AppendLine(KV("Fuel Expense:", string.IsNullOrWhiteSpace(FuelExpenseInput.Text) ? "0" : FuelExpenseInput.Text));
            sb.AppendLine(KV("Other Expense:", string.IsNullOrWhiteSpace(OtherExpenseInput.Text) ? "0" : OtherExpenseInput.Text));
            sb.AppendLine(KV("Total Expenses:", TotalExpensesText.Text));
            sb.AppendLine(KV("Net Cash Required:", NetCashRequiredText.Text));
            sb.AppendLine();

            sb.AppendLine("CASH SUMMARY (Notes)");
            sb.AppendLine(new string('-', 55));
            int n5 = ParseInt(Notes5Input.Text);
            int n10 = ParseInt(Notes10Input.Text);
            int n20 = ParseInt(Notes20Input.Text);
            int n50 = ParseInt(Notes50Input.Text);
            int n100 = ParseInt(Notes100Input.Text);
            int n200 = ParseInt(Notes200Input.Text);
            int n500 = ParseInt(Notes500Input.Text);
            sb.AppendLine("Denom   Count   Amount");
            sb.AppendLine("-----   -----   ------");
            sb.AppendLine($"₹5      {n5,-5}   ₹{(n5 * 5):F2}");
            sb.AppendLine($"₹10     {n10,-5}   ₹{(n10 * 10):F2}");
            sb.AppendLine($"₹20     {n20,-5}   ₹{(n20 * 20):F2}");
            sb.AppendLine($"₹50     {n50,-5}   ₹{(n50 * 50):F2}");
            sb.AppendLine($"₹100    {n100,-5}   ₹{(n100 * 100):F2}");
            sb.AppendLine($"₹200    {n200,-5}   ₹{(n200 * 200):F2}");
            sb.AppendLine($"₹500    {n500,-5}   ₹{(n500 * 500):F2}");
            sb.AppendLine();
            sb.AppendLine(KV("Final Cash (from Notes):", CashCalculatorTotal.Text));

            sb.AppendLine();
            sb.AppendLine(KV("Generated At:", DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt")));
            return sb.ToString();
        }

        private FlowDocument BuildAccountingSummaryFlowDocument()
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12
            };

            static int ParseInt(string? s) => int.TryParse(s, out var v) ? v : 0;
            static decimal ParseMoney(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return 0m;
                var cleaned = s.Replace("₹", "").Trim();
                return decimal.TryParse(cleaned, out var v) ? v : 0m;
            }

            void AddHeader(string text)
            {
                doc.Blocks.Add(new Paragraph(new Run(text))
                {
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 10)
                });
            }

            void AddSectionTitle(string text)
            {
                doc.Blocks.Add(new Paragraph(new Run(text))
                {
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 12, 0, 6)
                });
            }

            Table NewTable(int columns)
            {
                var table = new Table
                {
                    CellSpacing = 0,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                for (int i = 0; i < columns; i++) table.Columns.Add(new TableColumn());
                return table;
            }

            void AddKeyValue(TableRowGroup trg, string label, string value)
            {
                var row = new TableRow();
                row.Cells.Add(new TableCell(new Paragraph(new Run(label)) { Margin = new Thickness(0) })
                {
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(0, 2, 14, 2)
                });
                row.Cells.Add(new TableCell(new Paragraph(new Run(value)) { Margin = new Thickness(0) })
                {
                    Padding = new Thickness(0, 2, 0, 2)
                });
                trg.Rows.Add(row);
            }

            void AddHeaderRow(TableRowGroup trg, params string[] headers)
            {
                var row = new TableRow { FontWeight = FontWeights.Bold };
                foreach (var h in headers)
                {
                    row.Cells.Add(new TableCell(new Paragraph(new Run(h)) { Margin = new Thickness(0) })
                    {
                        Padding = new Thickness(0, 2, 12, 4),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        BorderBrush = Brushes.LightGray
                    });
                }
                trg.Rows.Add(row);
            }

            void AddRow(TableRowGroup trg, params string[] values)
            {
                var row = new TableRow();
                foreach (var v in values)
                {
                    row.Cells.Add(new TableCell(new Paragraph(new Run(v)) { Margin = new Thickness(0) })
                    {
                        Padding = new Thickness(0, 2, 12, 2)
                    });
                }
                trg.Rows.Add(row);
            }

            string date = AcctDatePicker.SelectedDate?.ToString("dd-MMM-yyyy") ?? "N/A";
            string driver = (AcctDriverCombo.SelectedItem as DriverComboItem)?.Name
                ?? (AcctDriverCombo.SelectedItem as Driver)?.Name ?? "N/A";
            string vehicle = (AcctVehicleCombo.SelectedItem as VehicleComboItem)?.VehicleNumber
                ?? (AcctVehicleCombo.SelectedItem as Vehicle)?.VehicleNumber ?? "N/A";

            int soldCyl = ParseInt(CylindersSoldText.Text);
            int returnedFilled = ParseInt(UnsoldFilledText.Text);
            int upliftCyl = soldCyl + returnedFilled;

            decimal totalCash = ParseMoney(TotalCashText.Text);
            decimal totalOnline = ParseMoney(TotalOnlineText.Text);
            decimal totalGPay = ParseMoney(TotalGooglePayText.Text);
            decimal totalCollection = totalCash + totalOnline + totalGPay;

            AddHeader("Accounting Summary");

            var infoTable = NewTable(2);
            var infoGroup = new TableRowGroup();
            infoTable.RowGroups.Add(infoGroup);
            AddKeyValue(infoGroup, "Date", date);
            AddKeyValue(infoGroup, "Driver", driver);
            AddKeyValue(infoGroup, "Vehicle", vehicle);
            doc.Blocks.Add(infoTable);

            AddSectionTitle("Cylinder Summary");
            var cylTable = NewTable(2);
            var cylGroup = new TableRowGroup();
            cylTable.RowGroups.Add(cylGroup);
            AddKeyValue(cylGroup, "Total Uplift Cylinders", upliftCyl.ToString());
            AddKeyValue(cylGroup, "Return Filled Cylinders", returnedFilled.ToString());
            AddKeyValue(cylGroup, "Sold Cylinders", soldCyl.ToString());
            AddKeyValue(cylGroup, "Empties Returned", EmptiesReturnedText.Text);
            doc.Blocks.Add(cylTable);

            AddSectionTitle("Refill Details (Price / Qty)");
            var refillTable = NewTable(6);
            var refillGroup = new TableRowGroup();
            refillTable.RowGroups.Add(refillGroup);
            AddHeaderRow(refillGroup, "Tier", "Price", "Qty", "Online Qty", "GPay Qty", "Cash Qty");
            AddRow(refillGroup, "R1", Refill1PriceInput.Text, Refill1QtyInput.Text, Refill1OnlineQtyInput.Text, Refill1GPayQtyInput.Text, Refill1CashQtyOutput.Text);
            AddRow(refillGroup, "R2", Refill2PriceInput.Text, Refill2QtyInput.Text, Refill2OnlineQtyInput.Text, Refill2GPayQtyInput.Text, Refill2CashQtyOutput.Text);
            AddRow(refillGroup, "R3", Refill3PriceInput.Text, Refill3QtyInput.Text, Refill3OnlineQtyInput.Text, Refill3GPayQtyInput.Text, Refill3CashQtyOutput.Text);
            doc.Blocks.Add(refillTable);

            var refillTotals = NewTable(2);
            var refillTotalsGroup = new TableRowGroup();
            refillTotals.RowGroups.Add(refillTotalsGroup);
            AddKeyValue(refillTotalsGroup, "Refills Count", RefillsCountText.Text);
            AddKeyValue(refillTotalsGroup, "Refill Total Amount", RefillTotalAmountOutput.Text);
            doc.Blocks.Add(refillTotals);

            AddSectionTitle("Payment Summary");
            var payTable = NewTable(2);
            var payGroup = new TableRowGroup();
            payTable.RowGroups.Add(payGroup);
            AddKeyValue(payGroup, "Online Payment (Total)", TotalOnlineText.Text);
            AddKeyValue(payGroup, "Google Pay (Total)", TotalGooglePayText.Text);
            AddKeyValue(payGroup, "Cash Collection (Total)", TotalCashText.Text);
            AddKeyValue(payGroup, "Final Total Collection", $"₹{totalCollection:F2}");
            doc.Blocks.Add(payTable);

            AddSectionTitle("Expenses");
            var expTable = NewTable(2);
            var expGroup = new TableRowGroup();
            expTable.RowGroups.Add(expGroup);
            AddKeyValue(expGroup, "Change Provided by Office", string.IsNullOrWhiteSpace(ChangeProvidedInput.Text) ? "0" : ChangeProvidedInput.Text);
            AddKeyValue(expGroup, "Fuel Expense", string.IsNullOrWhiteSpace(FuelExpenseInput.Text) ? "0" : FuelExpenseInput.Text);
            AddKeyValue(expGroup, "Other Expense", string.IsNullOrWhiteSpace(OtherExpenseInput.Text) ? "0" : OtherExpenseInput.Text);
            AddKeyValue(expGroup, "Total Expenses", TotalExpensesText.Text);
            AddKeyValue(expGroup, "Net Cash Required", NetCashRequiredText.Text);
            doc.Blocks.Add(expTable);

            AddSectionTitle("Cash Summary (Notes)");
            int n5 = ParseInt(Notes5Input.Text);
            int n10 = ParseInt(Notes10Input.Text);
            int n20 = ParseInt(Notes20Input.Text);
            int n50 = ParseInt(Notes50Input.Text);
            int n100 = ParseInt(Notes100Input.Text);
            int n200 = ParseInt(Notes200Input.Text);
            int n500 = ParseInt(Notes500Input.Text);

            var notesTable = NewTable(3);
            var notesGroup = new TableRowGroup();
            notesTable.RowGroups.Add(notesGroup);
            AddHeaderRow(notesGroup, "Denomination", "Count", "Amount");
            AddRow(notesGroup, "₹5", n5.ToString(), $"₹{(n5 * 5):F2}");
            AddRow(notesGroup, "₹10", n10.ToString(), $"₹{(n10 * 10):F2}");
            AddRow(notesGroup, "₹20", n20.ToString(), $"₹{(n20 * 20):F2}");
            AddRow(notesGroup, "₹50", n50.ToString(), $"₹{(n50 * 50):F2}");
            AddRow(notesGroup, "₹100", n100.ToString(), $"₹{(n100 * 100):F2}");
            AddRow(notesGroup, "₹200", n200.ToString(), $"₹{(n200 * 200):F2}");
            AddRow(notesGroup, "₹500", n500.ToString(), $"₹{(n500 * 500):F2}");
            doc.Blocks.Add(notesTable);

            var cashTotals = NewTable(2);
            var cashTotalsGroup = new TableRowGroup();
            cashTotals.RowGroups.Add(cashTotalsGroup);
            AddKeyValue(cashTotalsGroup, "Final Cash (from Notes)", CashCalculatorTotal.Text);
            doc.Blocks.Add(cashTotals);

            doc.Blocks.Add(new Paragraph(new Run($"Generated At: {DateTime.Now:dd-MMM-yyyy hh:mm tt}"))
            {
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 10,
                Foreground = Brushes.Gray
            });

            return doc;
        }

        private sealed class AccountingTabSnapshot
        {
            public DateTime SavedAt { get; set; }
            public DateTime? Date { get; set; }
            public int? DriverId { get; set; }
            public int? VehicleId { get; set; }
            public Dictionary<string, string> Fields { get; set; } = new();
        }
    }
}